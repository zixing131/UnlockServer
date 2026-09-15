using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnlockServer.Models;
using UnlockServer;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using InTheHand.Net.Sockets;

namespace UnlockServer.Services
{
    /// <summary>
    /// 设备发现：DeviceWatcher + BLE 广播扫描 + 已配对枚举。
    /// 蓝牙类型 0=全部, 1=经典, 2=BLE。
    /// </summary>
    public class BluetoothService : IBluetoothService, IDisposable
    {
        private static readonly string[] RequestedProperties =
        {
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.IsPaired",
            "System.Devices.Aep.SignalStrength",
            "System.ItemNameDisplay"
        };

        private const string SignalStrengthProperty = "System.Devices.Aep.SignalStrength";
        private const string DeviceAddressProperty = "System.Devices.Aep.DeviceAddress";
        private const string IsPairedProperty = "System.Devices.Aep.IsPaired";
        private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
        private const string BluetoothClassicId = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")";
        private const string BluetoothLEId = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

        private const int DeviceTimeoutSeconds = 45;

        private static readonly BluetoothService _shared = new BluetoothService();
        public static BluetoothService Shared => _shared;

        private DeviceWatcher _classicWatcher;
        private DeviceWatcher _leWatcher;
        private BluetoothLEAdvertisementWatcher _advWatcher;
        private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices;
        private readonly ConcurrentDictionary<string, short> _lastWatcherRssi =
            new ConcurrentDictionary<string, short>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _pinned =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly object _scanLock = new object();
        private readonly System.Timers.Timer _cleanupTimer;
        private readonly System.Timers.Timer _keepAliveTimer;
        private int _bluetoothType = 2;
        private bool _isDisposed;
        private int _scanUsers;
        private DateTime _lastAdvertisement = DateTime.MinValue;
        private DateTime _startedAt = DateTime.MinValue;
        private DateTime _lastAdvRestart = DateTime.MinValue;
        private DateTime _holdUntil = DateTime.MinValue;
        private int _refreshingPinned;

        public bool IsScanning { get; private set; }

        /// <summary>0=全部 1=经典 2=BLE</summary>
        public int BluetoothType
        {
            get => _bluetoothType;
            set
            {
                if (_bluetoothType == value) return;
                _bluetoothType = value;
                if (IsScanning)
                    RestartWatchers("type changed");
            }
        }

        public event EventHandler<BluetoothDeviceModel> DeviceDiscovered;
        public event EventHandler<BluetoothDeviceModel> DeviceUpdated;
        public event EventHandler<string> DeviceLost;

        public BluetoothService()
        {
            _devices = new ConcurrentDictionary<string, BluetoothDeviceModel>(StringComparer.OrdinalIgnoreCase);
            _cleanupTimer = new System.Timers.Timer(5000);
            _cleanupTimer.Elapsed += CleanupTimer_Elapsed;
            _keepAliveTimer = new System.Timers.Timer(5000);
            _keepAliveTimer.Elapsed += KeepAliveTimer_Elapsed;
        }

        public bool IsAdvertisementAlive
        {
            get
            {
                if (HasConnectedPinned()) return true;
                if (_lastAdvertisement == DateTime.MinValue)
                    return _startedAt != DateTime.MinValue && (DateTime.Now - _startedAt).TotalSeconds < 20;
                return (DateTime.Now - _lastAdvertisement).TotalSeconds <= 20;
            }
        }

        public bool IsRefreshing => DateTime.Now < _holdUntil;

        public int ScanSilenceSeconds
        {
            get
            {
                var from = _lastAdvertisement != DateTime.MinValue ? _lastAdvertisement : _startedAt;
                if (from == DateTime.MinValue) return int.MaxValue;
                return (int)(DateTime.Now - from).TotalSeconds;
            }
        }

        /// <summary>绑定设备不会被超时清理</summary>
        public void PinAddress(string address)
        {
            var n = BluetoothDiscover.NormalizeAddress(address);
            if (!string.IsNullOrEmpty(n))
                _pinned[n] = 1;
        }

        public void PinAddresses(IEnumerable<string> addresses)
        {
            _pinned.Clear();
            if (addresses == null) return;
            foreach (var raw in addresses)
            {
                var n = BluetoothDiscover.NormalizeAddress(raw);
                if (!string.IsNullOrEmpty(n))
                    _pinned[n] = 1;
            }
        }

        public bool IsAddressConnected(string address)
        {
            var n = BluetoothDiscover.NormalizeAddress(address);
            if (string.IsNullOrEmpty(n)) return false;
            return _devices.TryGetValue(n, out var d) && d.IsConnected;
        }

        public bool LooksLikeScanStall()
        {
            return IsRefreshing || (!IsAdvertisementAlive && !HasConnectedPinned());
        }

        public void AddScanUser()
        {
            lock (_scanLock)
            {
                _scanUsers++;
                if (!IsScanning)
                    StartScan();
            }
        }

        public void RemoveScanUser()
        {
            lock (_scanLock)
            {
                _scanUsers--;
                if (_scanUsers <= 0)
                {
                    _scanUsers = 0;
                    StopScan();
                }
            }
        }

        public void NotifySessionLocked()
        {
            if (!IsAdvertisementAlive)
                RestartAdvertisementWatcher("session lock");
        }

        public void NotifySessionUnlocked()
        {
            RestartWatchers("session unlock");
        }

        public void StartScan()
        {
            if (IsScanning) return;

            try
            {
                StopWatchers();
                RetainPinnedAndPaired();
                _startedAt = DateTime.Now;
                _lastAdvertisement = DateTime.MinValue;

                LoadPairedDevices();
                StartWatchersInternal();

                _cleanupTimer.Start();
                _keepAliveTimer.Start();
                IsScanning = true;
                LogHelper.WriteLine($"设备扫描已启动，类型: {TypeLabel(_bluetoothType)}");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动蓝牙扫描失败: {ex.Message}");
                IsScanning = false;
            }
        }

        public void StopScan()
        {
            StopWatchers();
            _cleanupTimer?.Stop();
            _keepAliveTimer?.Stop();
            IsScanning = false;
            LogHelper.WriteLine("蓝牙扫描已停止");
        }

        public void RestartWatchers(string reason = "refresh")
        {
            if (!IsScanning) return;
            lock (_scanLock)
            {
                if ((DateTime.Now - _lastAdvRestart).TotalSeconds < 4)
                    return;
                _lastAdvRestart = DateTime.Now;
                _holdUntil = DateTime.Now.AddSeconds(5);
            }
            LogHelper.WriteLine($"重启蓝牙扫描: {reason}");
            try
            {
                StopWatchers();
                StartWatchersInternal();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"重启蓝牙扫描失败: {ex.Message}");
            }
        }

        public List<BluetoothDeviceModel> GetDevices()
        {
            return _devices.Values
                .OrderByDescending(d => d.IsPaired)
                .ThenByDescending(d => d.Rssi)
                .ToList();
        }

        public BluetoothDeviceModel GetDeviceByAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            var normalizedAddress = BluetoothDiscover.NormalizeAddress(address);
            if (string.IsNullOrEmpty(normalizedAddress)) return null;
            _devices.TryGetValue(normalizedAddress, out var device);
            return device;
        }

        public BluetoothDeviceModel AddManualDevice(string address, string name = "")
        {
            var normalized = BluetoothDiscover.NormalizeAddress(address);
            if (string.IsNullOrEmpty(normalized)) return null;

            var device = new BluetoothDeviceModel
            {
                Name = string.IsNullOrEmpty(name) ? "手动添加" : name,
                Address = normalized,
                Rssi = -100,
                Type = _bluetoothType == 1 ? "Classic" : "BLE",
                IsPaired = false,
                LastSeen = DateTime.Now
            };

            _devices.AddOrUpdate(normalized, device, (_, existing) =>
            {
                if (!string.IsNullOrEmpty(name))
                    existing.Name = name;
                return existing;
            });

            PinAddress(normalized);
            DeviceDiscovered?.Invoke(this, device);
            return device;
        }

        #region 扫描实现

        private DeviceWatcher CreateWatcher(string selector, string type)
        {
            var watcher = DeviceInformation.CreateWatcher(
                selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);

            watcher.Added += (s, info) => OnWatcherAdded(info, type);
            watcher.Updated += (s, update) => OnWatcherUpdated(update, type);
            watcher.Removed += (s, update) => { /* 周期性 Removed 忽略 */ };
            watcher.EnumerationCompleted += (s, e) =>
                LogHelper.WriteLine($"{type} 枚举完成，当前 {_devices.Count} 个设备");
            watcher.Start();
            return watcher;
        }

        private void StartWatchersInternal()
        {
            if (_bluetoothType == 1 || _bluetoothType == 0)
                _classicWatcher = CreateWatcher(BluetoothClassicId, "Classic");

            if (_bluetoothType == 2 || _bluetoothType == 0)
            {
                _leWatcher = CreateWatcher(BluetoothLEId, "BLE");
                StartAdvertisementWatcher();
            }
        }

        private void StartAdvertisementWatcher()
        {
            try
            {
                _advWatcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = BluetoothLEScanningMode.Active
                };
                _advWatcher.Received += AdvWatcher_Received;
                _advWatcher.Start();
                LogHelper.WriteLine("BLE 广播扫描（Active）已启动");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动广播扫描失败: {ex.Message}");
            }
        }

        private void RestartAdvertisementWatcher(string reason)
        {
            if (_bluetoothType == 1) return;
            lock (_scanLock)
            {
                if ((DateTime.Now - _lastAdvRestart).TotalSeconds < 8)
                    return;
                _lastAdvRestart = DateTime.Now;
                _holdUntil = DateTime.Now.AddSeconds(5);
            }
            LogHelper.WriteLine($"刷新 BLE 广播扫描: {reason}");
            try
            {
                if (_advWatcher != null)
                {
                    try
                    {
                        _advWatcher.Received -= AdvWatcher_Received;
                        if (_advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                            _advWatcher.Stop();
                    }
                    catch { }
                    _advWatcher = null;
                }
                StartAdvertisementWatcher();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"刷新广播扫描失败: {ex.Message}");
            }
        }

        private void AdvWatcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            _lastAdvertisement = DateTime.Now;
            var address = BluetoothDiscover.FormatFromUlong(args.BluetoothAddress);
            if (string.IsNullOrEmpty(address)) return;

            var name = args.Advertisement?.LocalName;
            Upsert(address, name, args.RawSignalStrengthInDBm, "BLE", false, true);
        }

        private void OnWatcherAdded(DeviceInformation deviceInfo, string type)
        {
            var address = GetAddressFromDeviceInfo(deviceInfo);
            if (string.IsNullOrEmpty(address)) return;

            var isPaired = GetIsPaired(deviceInfo.Properties);
            short rssi = -100;
            var hasRssi = TryGetRssi(deviceInfo.Properties, out var parsed);
            if (hasRssi)
                rssi = parsed;

            if (HasBool(deviceInfo.Properties, IsConnectedProperty))
                SetConnected(address, GetIsConnected(deviceInfo.Properties));

            var refresh = hasRssi && (IsPinned(address) || IsNewWatcherRssi(address, rssi));
            if (hasRssi && IsPinned(address))
                _lastWatcherRssi[address] = rssi;
            Upsert(address, deviceInfo.Name, rssi, type, isPaired, refresh || IsAddressConnected(address));
        }

        private void OnWatcherUpdated(DeviceInformationUpdate update, string type)
        {
            var address = BluetoothDiscover.NormalizeAddress(update.Id);
            if (string.IsNullOrEmpty(address))
                address = BluetoothDiscover.NormalizeAddress(
                    update.Properties != null &&
                    update.Properties.TryGetValue(DeviceAddressProperty, out var addrObj)
                        ? addrObj?.ToString()
                        : null);
            if (string.IsNullOrEmpty(address)) return;

            if (HasBool(update.Properties, IsConnectedProperty))
                SetConnected(address, GetIsConnected(update.Properties));

            if (TryGetRssi(update.Properties, out var rssi))
            {
                var refresh = IsPinned(address) || IsNewWatcherRssi(address, rssi);
                if (IsPinned(address))
                    _lastWatcherRssi[address] = rssi;
                if (refresh)
                    Upsert(address, null, rssi, type, false, true);
            }
            else if (IsAddressConnected(address))
            {
                Upsert(address, null, -50, type, false, true);
            }
        }

        private bool IsNewWatcherRssi(string address, short rssi)
        {
            if (_lastWatcherRssi.TryGetValue(address, out var prev) && prev == rssi)
                return false;
            _lastWatcherRssi[address] = rssi;
            return true;
        }

        private bool IsPinned(string address)
        {
            return !string.IsNullOrEmpty(address) && _pinned.ContainsKey(address);
        }

        private void SetConnected(string address, bool connected)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (_devices.TryGetValue(address, out var device))
                device.IsConnected = connected;
            else if (connected)
                Upsert(address, null, -50, "BLE", true, true);
        }

        private bool HasConnectedPinned()
        {
            foreach (var addr in _pinned.Keys)
            {
                if (IsAddressConnected(addr))
                    return true;
            }
            return false;
        }

        private void KeepAliveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!IsScanning) return;
            if (!IsAdvertisementAlive)
                RestartAdvertisementWatcher("ads silent");
            RefreshPinnedPresence();
        }

        private void RefreshPinnedPresence()
        {
            if (_pinned.IsEmpty) return;
            if (Interlocked.Exchange(ref _refreshingPinned, 1) == 1) return;

            Task.Run(async () =>
            {
                try
                {
                    foreach (var address in _pinned.Keys)
                    {
                        if (!IsScanning) return;
                        if (_devices.TryGetValue(address, out var existing) &&
                            existing.LastSeen != DateTime.MinValue &&
                            (DateTime.Now - existing.LastSeen).TotalSeconds < 4)
                            continue;

                        var ul = BluetoothDiscover.ParseBluetoothAddress(address);
                        if (ul == 0) continue;
                        BluetoothLEDevice dev = null;
                        try
                        {
                            dev = await BluetoothLEDevice.FromBluetoothAddressAsync(ul);
                            if (dev == null) continue;
                            var connected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                            SetConnected(address, connected);
                            if (connected)
                                Upsert(address, dev.Name, existing?.Rssi > -100 ? existing.Rssi : (short)-50, "BLE", true, true);
                        }
                        catch { }
                        finally
                        {
                            try { dev?.Dispose(); } catch { }
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshingPinned, 0);
                }
            });
        }

        private void Upsert(string address, string name, short rssi, string type, bool isPaired, bool refreshSeen)
        {
            var isNew = !_devices.ContainsKey(address);
            var device = _devices.AddOrUpdate(address,
                new BluetoothDeviceModel
                {
                    Name = name ?? "",
                    Address = address,
                    Rssi = rssi,
                    Type = type,
                    IsPaired = isPaired,
                    LastSeen = refreshSeen ? DateTime.Now : DateTime.MinValue
                },
                (_, existing) =>
                {
                    if (!string.IsNullOrEmpty(name))
                        existing.Name = name;
                    if (rssi > -100)
                        existing.Rssi = rssi;
                    if (isPaired)
                        existing.IsPaired = true;
                    if (refreshSeen)
                        existing.LastSeen = DateTime.Now;
                    if (!string.IsNullOrEmpty(type))
                        existing.Type = type;
                    if (IsAddressConnected(address))
                        existing.IsConnected = true;
                    return existing;
                });

            if (isNew)
            {
                DeviceDiscovered?.Invoke(this, device);
                LogHelper.WriteLine($"发现设备: {device.DisplayName} [{address}] {rssi}dBm {(isPaired ? "(已配对)" : "")}");
            }
            else if (refreshSeen)
            {
                DeviceUpdated?.Invoke(this, device);
            }
        }

        private void LoadPairedDevices()
        {
            Task.Run(() =>
            {
                try
                {
                    if (_bluetoothType == 1 || _bluetoothType == 0)
                        LoadClassicPaired();
                    if (_bluetoothType == 2 || _bluetoothType == 0)
                        LoadBlePaired();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLine($"加载已配对设备失败: {ex.Message}");
                }
            });
        }

        private void LoadClassicPaired()
        {
            try
            {
                using (var client = new BluetoothClient())
                {
                    foreach (var device in client.PairedDevices)
                    {
                        var address = BluetoothDiscover.FormatMacAddress(device.DeviceAddress.ToString());
                        if (string.IsNullOrEmpty(address)) continue;
                        Upsert(address, device.DeviceName, -100, "Classic", true, false);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"加载经典已配对失败: {ex.Message}");
            }
        }

        private void LoadBlePaired()
        {
            try
            {
                var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
                var found = DeviceInformation.FindAllAsync(selector).AsTask().GetAwaiter().GetResult();
                foreach (var info in found)
                {
                    var address = GetAddressFromDeviceInfo(info) ?? BluetoothDiscover.NormalizeAddress(info.Id);
                    if (string.IsNullOrEmpty(address)) continue;
                    Upsert(address, info.Name, -100, "BLE", true, false);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"加载 BLE 已配对失败: {ex.Message}");
            }
        }

        private void RetainPinnedAndPaired()
        {
            var keep = _devices
                .Where(kv => kv.Value.IsPaired || IsPinned(kv.Key))
                .ToList();

            _devices.Clear();
            foreach (var kv in keep)
                _devices[kv.Key] = kv.Value;
        }

        private void CleanupTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            var timeoutDevices = _devices
                .Where(kv =>
                    !kv.Value.IsPaired &&
                    !IsPinned(kv.Key) &&
                    kv.Value.LastSeen != DateTime.MinValue &&
                    (now - kv.Value.LastSeen).TotalSeconds > DeviceTimeoutSeconds)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var address in timeoutDevices)
            {
                if (_devices.TryRemove(address, out var device))
                {
                    DeviceLost?.Invoke(this, address);
                    LogHelper.WriteLine($"设备超时移除: {device.DisplayName} [{address}]");
                }
            }
        }

        #endregion

        #region 辅助

        private void StopWatchers()
        {
            StopOneWatcher(ref _classicWatcher);
            StopOneWatcher(ref _leWatcher);

            if (_advWatcher != null)
            {
                try
                {
                    _advWatcher.Received -= AdvWatcher_Received;
                    if (_advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
                        _advWatcher.Stop();
                }
                catch { }
                _advWatcher = null;
            }
        }

        private static void StopOneWatcher(ref DeviceWatcher watcher)
        {
            if (watcher == null) return;
            try
            {
                if (watcher.Status == DeviceWatcherStatus.Started ||
                    watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }
            catch { }
            watcher = null;
        }

        private static string TypeLabel(int type)
        {
            switch (type)
            {
                case 0: return "全部";
                case 2: return "BLE";
                default: return "经典蓝牙";
            }
        }

        private static string GetAddressFromDeviceInfo(DeviceInformation deviceInfo)
        {
            string raw = null;
            if (deviceInfo.Properties != null &&
                deviceInfo.Properties.TryGetValue(DeviceAddressProperty, out var addrObj))
            {
                raw = addrObj?.ToString();
            }

            var norm = BluetoothDiscover.NormalizeAddress(raw);
            if (!string.IsNullOrEmpty(norm)) return norm;
            return BluetoothDiscover.NormalizeAddress(deviceInfo.Id);
        }

        private static bool TryGetRssi(IReadOnlyDictionary<string, object> props, out short rssi)
        {
            rssi = 0;
            if (props == null) return false;
            if (!props.TryGetValue(SignalStrengthProperty, out var val) || val == null) return false;
            try
            {
                var n = Convert.ToInt32(val);
                rssi = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, n));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool GetIsPaired(IReadOnlyDictionary<string, object> props)
        {
            return GetBool(props, IsPairedProperty);
        }

        private static bool GetIsConnected(IReadOnlyDictionary<string, object> props)
        {
            return GetBool(props, IsConnectedProperty);
        }

        private static bool GetBool(IReadOnlyDictionary<string, object> props, string key)
        {
            if (props == null) return false;
            if (!props.TryGetValue(key, out var val) || val == null) return false;
            try { return Convert.ToBoolean(val); }
            catch { return false; }
        }

        private static bool HasBool(IReadOnlyDictionary<string, object> props, string key)
        {
            return props != null && props.TryGetValue(key, out var val) && val != null;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopScan();
            _cleanupTimer?.Dispose();
            _keepAliveTimer?.Dispose();
            _devices.Clear();
        }

        #endregion
    }
}
