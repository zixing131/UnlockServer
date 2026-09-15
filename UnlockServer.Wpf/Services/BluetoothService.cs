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
        private readonly ConcurrentDictionary<string, BluetoothLEDevice> _heldLe =
            new ConcurrentDictionary<string, BluetoothLEDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, BluetoothDevice> _heldClassic =
            new ConcurrentDictionary<string, BluetoothDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly object _scanLock = new object();
        private readonly System.Timers.Timer _cleanupTimer;
        private readonly System.Timers.Timer _keepAliveTimer;
        private int _bluetoothType = 2;
        private bool _isDisposed;
        private int _scanUsers;
        private DateTime _lastAdvertisement = DateTime.MinValue;
        private DateTime _lastWatcherActivity = DateTime.MinValue;
        private DateTime _startedAt = DateTime.MinValue;
        private DateTime _lastAdvRestart = DateTime.MinValue;
        private DateTime _holdUntil = DateTime.MinValue;
        private DateTime _stallSince = DateTime.MinValue;
        private int _stallRestarts;
        private int _refreshingPinned;
        private const int StallHoldSeconds = 12;
        private const int MaxStallRestarts = 1;

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
            _keepAliveTimer = new System.Timers.Timer(3000);
            _keepAliveTimer.Elapsed += KeepAliveTimer_Elapsed;
        }

        public bool IsAdvertisementAlive
        {
            get
            {
                if (HasConnectedPinned()) return true;
                var last = _lastAdvertisement != DateTime.MinValue ? _lastAdvertisement : DateTime.MinValue;
                var watcher = _lastWatcherActivity != DateTime.MinValue ? _lastWatcherActivity : DateTime.MinValue;
                var newest = last > watcher ? last : watcher;
                if (newest == DateTime.MinValue)
                    return _startedAt != DateTime.MinValue && (DateTime.Now - _startedAt).TotalSeconds < 20;
                return (DateTime.Now - newest).TotalSeconds <= 20;
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
            if (addresses != null)
            {
                foreach (var raw in addresses)
                {
                    var n = BluetoothDiscover.NormalizeAddress(raw);
                    if (!string.IsNullOrEmpty(n))
                        _pinned[n] = 1;
                }
            }

            ReleaseUnpinnedHolds();
            if (IsScanning)
                AttachPinnedDevices();
        }

        public bool IsAddressConnected(string address)
        {
            var n = BluetoothDiscover.NormalizeAddress(address);
            if (string.IsNullOrEmpty(n)) return false;

            if (_heldLe.TryGetValue(n, out var le))
            {
                try
                {
                    if (le.ConnectionStatus == BluetoothConnectionStatus.Connected)
                        return true;
                }
                catch { }
            }

            if (_heldClassic.TryGetValue(n, out var classic))
            {
                try
                {
                    if (classic.ConnectionStatus == BluetoothConnectionStatus.Connected)
                        return true;
                }
                catch { }
            }

            return _devices.TryGetValue(n, out var d) && d.IsConnected;
        }

        public bool LooksLikeScanStall()
        {
            if (HasConnectedPinned())
            {
                ClearStall();
                return false;
            }

            if (IsAdvertisementAlive)
            {
                ClearStall();
                return IsRefreshing;
            }

            if (_stallSince == DateTime.MinValue)
                _stallSince = DateTime.Now;

            return (DateTime.Now - _stallSince).TotalSeconds < StallHoldSeconds;
        }

        private void ClearStall()
        {
            _stallSince = DateTime.MinValue;
            _stallRestarts = 0;
        }

        private void NoteWatcherActivity()
        {
            _lastWatcherActivity = DateTime.Now;
            ClearStall();
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
                _lastWatcherActivity = DateTime.MinValue;
                ClearStall();

                LoadPairedDevices();
                StartWatchersInternal();
                AttachPinnedDevices();

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
            ReleaseHeldDevices();
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
                if ((DateTime.Now - _lastAdvRestart).TotalSeconds < 8)
                    return;
                if (_stallSince != DateTime.MinValue &&
                    (DateTime.Now - _stallSince).TotalSeconds >= StallHoldSeconds)
                    return;
                _lastAdvRestart = DateTime.Now;
                _holdUntil = DateTime.Now.AddSeconds(4);
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

                try
                {
                    _advWatcher.SignalStrengthFilter.InRangeThresholdInDBm = -90;
                    _advWatcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -100;
                    _advWatcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromSeconds(5);
                    _advWatcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromSeconds(1);
                }
                catch { }

                TryApplyLowLatency(_advWatcher);
                _advWatcher.Received += AdvWatcher_Received;
                _advWatcher.Start();
                LogHelper.WriteLine("BLE 广播扫描（Active + RSSI 滤波）已启动");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动广播扫描失败: {ex.Message}");
            }
        }

        private static void TryApplyLowLatency(BluetoothLEAdvertisementWatcher watcher)
        {
            try
            {
                var prop = watcher.GetType().GetProperty("ScanParameters");
                if (prop == null) return;
                var method = prop.PropertyType.GetMethod("LowLatency",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method == null) return;
                prop.SetValue(watcher, method.Invoke(null, null));
                LogHelper.WriteLine("BLE 扫描参数: LowLatency");
            }
            catch { }
        }

        private void RestartAdvertisementWatcher(string reason)
        {
            if (_bluetoothType == 1) return;
            lock (_scanLock)
            {
                if ((DateTime.Now - _lastAdvRestart).TotalSeconds < 8)
                    return;
                if (_stallSince != DateTime.MinValue &&
                    (DateTime.Now - _stallSince).TotalSeconds >= StallHoldSeconds)
                    return;
                if (_stallRestarts >= MaxStallRestarts)
                    return;
                _stallRestarts++;
                _lastAdvRestart = DateTime.Now;
                _holdUntil = DateTime.Now.AddSeconds(4);
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
            ClearStall();
            var address = BluetoothDiscover.FormatFromUlong(args.BluetoothAddress);
            if (string.IsNullOrEmpty(address)) return;

            var name = args.Advertisement?.LocalName;
            Upsert(address, name, args.RawSignalStrengthInDBm, "BLE", false, true);
        }

        private void OnWatcherAdded(DeviceInformation deviceInfo, string type)
        {
            var address = GetAddressFromDeviceInfo(deviceInfo);
            if (string.IsNullOrEmpty(address)) return;
            NoteWatcherActivity();

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
            NoteWatcherActivity();

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
            PollHeldConnections();
            if (_advWatcher != null &&
                (_advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted ||
                 _advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopped) &&
                LooksLikeScanStall())
            {
                RestartAdvertisementWatcher("watcher stopped");
            }
        }

        private void PollHeldConnections()
        {
            foreach (var kv in _heldLe)
            {
                try
                {
                    var connected = kv.Value.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    SetConnected(kv.Key, connected);
                    if (connected)
                        Upsert(kv.Key, kv.Value.Name, HeldRssi(kv.Key), "BLE", true, true);
                }
                catch { }
            }

            foreach (var kv in _heldClassic)
            {
                try
                {
                    var connected = kv.Value.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    SetConnected(kv.Key, connected);
                    if (connected)
                        Upsert(kv.Key, kv.Value.Name, HeldRssi(kv.Key), "Classic", true, true);
                }
                catch { }
            }
        }

        private short HeldRssi(string address)
        {
            if (_devices.TryGetValue(address, out var existing) && existing.Rssi > -100)
                return existing.Rssi;
            return -50;
        }

        private void AttachPinnedDevices()
        {
            if (_pinned.IsEmpty) return;
            if (Interlocked.Exchange(ref _refreshingPinned, 1) == 1) return;

            Task.Run(async () =>
            {
                try
                {
                    await AttachPairedLeAsync().ConfigureAwait(false);
                    await AttachPairedClassicAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLine($"绑定设备连接监听失败: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshingPinned, 0);
                }
            });
        }

        private async Task AttachPairedLeAsync()
        {
            var found = await DeviceInformation.FindAllAsync(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)).AsTask().ConfigureAwait(false);
            foreach (var info in found)
            {
                if (!IsScanning) return;
                var address = GetAddressFromDeviceInfo(info);
                if (!IsPinned(address) || _heldLe.ContainsKey(address)) continue;

                var dev = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask().ConfigureAwait(false);
                if (dev == null) continue;
                if (!_heldLe.TryAdd(address, dev))
                {
                    dev.Dispose();
                    continue;
                }

                dev.ConnectionStatusChanged += OnHeldLeConnectionChanged;
                LogHelper.WriteLine($"已监听 BLE 连接: {dev.Name}[{address}] {dev.ConnectionStatus}");
                var connected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                SetConnected(address, connected);
                if (connected)
                    Upsert(address, dev.Name, -50, "BLE", true, true);
            }
        }

        private async Task AttachPairedClassicAsync()
        {
            var found = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask().ConfigureAwait(false);
            foreach (var info in found)
            {
                if (!IsScanning) return;
                var address = GetAddressFromDeviceInfo(info);
                if (!IsPinned(address) || _heldClassic.ContainsKey(address)) continue;

                var dev = await BluetoothDevice.FromIdAsync(info.Id).AsTask().ConfigureAwait(false);
                if (dev == null) continue;
                if (!_heldClassic.TryAdd(address, dev))
                {
                    dev.Dispose();
                    continue;
                }

                dev.ConnectionStatusChanged += OnHeldClassicConnectionChanged;
                LogHelper.WriteLine($"已监听经典蓝牙连接: {dev.Name}[{address}] {dev.ConnectionStatus}");
                var connected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
                SetConnected(address, connected);
                if (connected)
                    Upsert(address, dev.Name, -50, "Classic", true, true);
            }
        }

        private void OnHeldLeConnectionChanged(BluetoothLEDevice sender, object args)
        {
            if (sender == null) return;
            var address = BluetoothDiscover.FormatFromUlong(sender.BluetoothAddress);
            var connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            SetConnected(address, connected);
            if (connected)
                Upsert(address, sender.Name, -50, "BLE", true, true);
            LogHelper.WriteLine($"BLE 连接变化: {address} {(connected ? "已连接" : "已断开")}");
        }

        private void OnHeldClassicConnectionChanged(BluetoothDevice sender, object args)
        {
            if (sender == null) return;
            var address = BluetoothDiscover.FormatFromUlong(sender.BluetoothAddress);
            var connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            SetConnected(address, connected);
            if (connected)
                Upsert(address, sender.Name, -50, "Classic", true, true);
            LogHelper.WriteLine($"经典蓝牙连接变化: {address} {(connected ? "已连接" : "已断开")}");
        }

        private void ReleaseUnpinnedHolds()
        {
            foreach (var key in _heldLe.Keys.ToArray())
            {
                if (!_pinned.ContainsKey(key))
                    ReleaseHeldLe(key);
            }
            foreach (var key in _heldClassic.Keys.ToArray())
            {
                if (!_pinned.ContainsKey(key))
                    ReleaseHeldClassic(key);
            }
        }

        private void ReleaseHeldDevices()
        {
            foreach (var key in _heldLe.Keys.ToArray())
                ReleaseHeldLe(key);
            foreach (var key in _heldClassic.Keys.ToArray())
                ReleaseHeldClassic(key);
        }

        private void ReleaseHeldLe(string address)
        {
            if (!_heldLe.TryRemove(address, out var dev) || dev == null) return;
            try { dev.ConnectionStatusChanged -= OnHeldLeConnectionChanged; } catch { }
            try { dev.Dispose(); } catch { }
        }

        private void ReleaseHeldClassic(string address)
        {
            if (!_heldClassic.TryRemove(address, out var dev) || dev == null) return;
            try { dev.ConnectionStatusChanged -= OnHeldClassicConnectionChanged; } catch { }
            try { dev.Dispose(); } catch { }
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
