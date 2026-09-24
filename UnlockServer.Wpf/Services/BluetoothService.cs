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
        private int _discoveryUsers;
        private int _watcherGeneration;
        private int EffectiveBluetoothType => _discoveryUsers > 0 ? 0 : _bluetoothType;
        private DateTime _lastAdvertisement = DateTime.MinValue;
        private DateTime _lastWatcherActivity = DateTime.MinValue;
        private DateTime _startedAt = DateTime.MinValue;
        private DateTime _lastAdvRestart = DateTime.MinValue;
        private DateTime _holdUntil = DateTime.MinValue;
        private DateTime _stallSince = DateTime.MinValue;
        private int _refreshingPinned;
        private const int StallHoldSeconds = 12;
        private const int RecoveryIntervalSeconds = 30;

        public bool IsScanning { get; private set; }

        /// <summary>0=全部 1=经典 2=BLE</summary>
        public int BluetoothType
        {
            get => _bluetoothType;
            set
            {
                lock (_scanLock)
                {
                    var oldType = EffectiveBluetoothType;
                    _bluetoothType = value;
                    if (IsScanning && oldType != EffectiveBluetoothType)
                        RestartWatchers("type changed", force: true);
                }
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
                // Device enumeration metadata is not proof that BLE advertisements are arriving.
                var newest = EffectiveBluetoothType == 1 ? _lastWatcherActivity : _lastAdvertisement;
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
            if (string.IsNullOrEmpty(n) || !IsScanning) return false;
            bool hasConnectionHandle = false;

            if (_heldLe.TryGetValue(n, out var le))
            {
                hasConnectionHandle = true;
                try
                {
                    if (le.ConnectionStatus == BluetoothConnectionStatus.Connected)
                        return true;
                }
                catch { }
            }

            if (_heldClassic.TryGetValue(n, out var classic))
            {
                hasConnectionHandle = true;
                try
                {
                    if (classic.ConnectionStatus == BluetoothConnectionStatus.Connected)
                        return true;
                }
                catch { }
            }

            return !hasConnectionHandle && _devices.TryGetValue(n, out var d) && d.IsConnected;
        }

        // A bounded presence grace, not the watchdog's retry condition. Silence
        // may mean the last device really left; recovery continues after grace ends.
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
        }

        private void NoteWatcherActivity()
        {
            _lastWatcherActivity = DateTime.Now;
        }

        public void AddScanUser(bool discovery = false)
        {
            lock (_scanLock)
            {
                if (_isDisposed) throw new ObjectDisposedException(nameof(BluetoothService));
                var oldType = EffectiveBluetoothType;
                _scanUsers++;
                if (discovery) _discoveryUsers++;
                if (!IsScanning) StartScan();
                else if (oldType != EffectiveBluetoothType)
                    RestartWatchers("discovery opened", force: true);
            }
        }

        public void RemoveScanUser(bool discovery = false)
        {
            lock (_scanLock)
            {
                if (_scanUsers == 0 || (discovery && _discoveryUsers == 0)) return;
                var oldType = EffectiveBluetoothType;
                _scanUsers--;
                if (discovery) _discoveryUsers--;
                if (_scanUsers == 0) StopScan();
                else if (oldType != EffectiveBluetoothType)
                    RestartWatchers("discovery closed", force: true);
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
            lock (_scanLock)
            {
                if (IsScanning || _isDisposed) return;
                StopWatchers();
                RetainPinnedAndPaired();
                _startedAt = DateTime.Now;
                _lastAdvertisement = DateTime.MinValue;
                _lastWatcherActivity = DateTime.MinValue;
                _lastAdvRestart = DateTime.MinValue;
                ClearStall();
                IsScanning = true;
                StartWatchersInternal();
                LoadPairedDevices();
                AttachPinnedDevices();
                _cleanupTimer.Start();
                _keepAliveTimer.Start();
                LogHelper.WriteLine($"设备扫描已启动，类型: {TypeLabel(EffectiveBluetoothType)}");
            }
        }

        public void StopScan()
        {
            lock (_scanLock)
            {
                IsScanning = false;
                _cleanupTimer.Stop();
                _keepAliveTimer.Stop();
                StopWatchers();
                ReleaseHeldDevices();
                foreach (var address in _devices.Keys) SetConnected(address, false);
                LogHelper.WriteLine("蓝牙扫描已停止");
            }
        }

        public void RestartWatchers(string reason = "refresh", bool force = false)
        {
            lock (_scanLock)
            {
                if (!IsScanning || _isDisposed) return;
                if (!force && (DateTime.Now - _lastAdvRestart).TotalSeconds < 8) return;
                _lastAdvRestart = DateTime.Now;
                // Repeated recovery must not indefinitely extend presence of an absent device.
                if (_stallSince == DateTime.MinValue) _holdUntil = DateTime.Now.AddSeconds(4);
                LogHelper.WriteLine($"重启蓝牙扫描: {reason}");
                StopWatchers();
                StartWatchersInternal();
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

        public BluetoothDeviceModel AddManualDevice(string address, string name = "", int? type = null)
        {
            var normalized = BluetoothDiscover.NormalizeAddress(address);
            if (string.IsNullOrEmpty(normalized)) return null;

            var device = new BluetoothDeviceModel
            {
                Name = string.IsNullOrEmpty(name) ? "手动添加" : name,
                Address = normalized,
                Rssi = -100,
                Type = (type ?? _bluetoothType) == 1 ? "Classic" : "BLE",
                IsPaired = false,
                LastSeen = DateTime.MinValue
            };

            device = _devices.AddOrUpdate(normalized, device, (_, existing) =>
            {
                var copy = existing.Copy();
                if (!string.IsNullOrEmpty(name)) copy.Name = name;
                if (type.HasValue) copy.Type = type == 1 ? "Classic" : "BLE";
                return copy;
            });

            DeviceDiscovered?.Invoke(this, device);
            return device;
        }

        #region 扫描实现

        private DeviceWatcher CreateWatcher(string selector, string type, int generation)
        {
            var watcher = DeviceInformation.CreateWatcher(
                selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);

            watcher.Added += (s, info) => { if (generation == _watcherGeneration && IsScanning) OnWatcherAdded(info, type); };
            watcher.Updated += (s, update) => { if (generation == _watcherGeneration && IsScanning) OnWatcherUpdated(update, type); };
            watcher.Removed += (s, update) => { /* 周期性 Removed 忽略 */ };
            watcher.EnumerationCompleted += (s, e) =>
                LogHelper.WriteLine($"{type} 枚举完成，当前 {_devices.Count} 个设备");
            watcher.Start();
            return watcher;
        }

        private void StartWatchersInternal()
        {
            var generation = _watcherGeneration;
            if (EffectiveBluetoothType != 2)
            {
                try { _classicWatcher = CreateWatcher(BluetoothClassicId, "Classic", generation); }
                catch (Exception ex) { LogHelper.WriteLine($"启动经典扫描失败: {ex.Message}"); }
            }
            if (EffectiveBluetoothType != 1)
            {
                try { _leWatcher = CreateWatcher(BluetoothLEId, "BLE", generation); }
                catch (Exception ex) { LogHelper.WriteLine($"启动 BLE 枚举失败: {ex.Message}"); }
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
            lock (_scanLock)
            {
                if (!IsScanning || _isDisposed || EffectiveBluetoothType == 1) return;
                if ((DateTime.Now - _lastAdvRestart).TotalSeconds < RecoveryIntervalSeconds) return;
                _lastAdvRestart = DateTime.Now;
                LogHelper.WriteLine($"恢复 BLE 广播扫描: {reason}");
                StopAdvertisementWatcher();
                StartAdvertisementWatcher();
            }
        }

        private void AdvWatcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (!IsScanning || !ReferenceEquals(sender, _advWatcher)) return;
            // 127 is the WinRT out-of-range sentinel, not a very strong signal.
            if (args.RawSignalStrengthInDBm == 127) return;
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
            // Added replays Windows' cache after each restart. Only subsequent Classic
            // RSSI updates or actual BLE packets count as new proximity observations.
            Upsert(address, deviceInfo.Name, -100, type, isPaired, false);
            if (HasBool(deviceInfo.Properties, IsConnectedProperty))
                SetConnected(address, GetIsConnected(deviceInfo.Properties));

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

            if (type == "Classic" && TryGetRssi(update.Properties, out var rssi) && rssi < 0 && rssi > -127)
                Upsert(address, null, rssi, type, false, true);
        }

        private bool IsPinned(string address)
        {
            return !string.IsNullOrEmpty(address) && _pinned.ContainsKey(address);
        }

        private void SetConnected(string address, bool connected)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (_devices.TryGetValue(address, out var device))
                _devices.AddOrUpdate(address, device, (_, current) =>
                {
                    var copy = current.Copy();
                    copy.IsConnected = connected;
                    return copy;
                });
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
            // Avoid overlapping timer callbacks when a driver is slow.
            if (!Monitor.TryEnter(_scanLock)) return;
            try
            {
                if (!IsScanning || _isDisposed) return;
                PollHeldConnections();
                bool enumerationStopped =
                    (EffectiveBluetoothType != 2 && WatcherStopped(_classicWatcher)) ||
                    (EffectiveBluetoothType != 1 && WatcherStopped(_leWatcher));
                if (enumerationStopped && (DateTime.Now - _lastAdvRestart).TotalSeconds >= RecoveryIntervalSeconds)
                    RestartWatchers("enumeration stopped");
                if (EffectiveBluetoothType != 1 &&
                    (_advWatcher == null || _advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted ||
                     _advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopped || ScanSilenceSeconds >= 20))
                    RestartAdvertisementWatcher("stopped or no advertisements");
            }
            catch (Exception ex) { LogHelper.WriteLine($"蓝牙扫描恢复失败: {ex.Message}"); }
            finally { Monitor.Exit(_scanLock); }
        }

        private static bool WatcherStopped(DeviceWatcher watcher)
        {
            return watcher == null || watcher.Status == DeviceWatcherStatus.Aborted ||
                   watcher.Status == DeviceWatcherStatus.Stopped;
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
                        Upsert(kv.Key, kv.Value.Name, -100, "BLE", true, false);
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
                        Upsert(kv.Key, kv.Value.Name, -100, "Classic", true, false);
                }
                catch { }
            }
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
                lock (_scanLock)
                {
                    if (!IsScanning || !IsPinned(address) || !_heldLe.TryAdd(address, dev))
                    {
                        dev.Dispose();
                        continue;
                    }
                    dev.ConnectionStatusChanged += OnHeldLeConnectionChanged;
                    Upsert(address, dev.Name, -100, "BLE", true, false);
                    SetConnected(address, dev.ConnectionStatus == BluetoothConnectionStatus.Connected);
                }
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
                lock (_scanLock)
                {
                    if (!IsScanning || !IsPinned(address) || !_heldClassic.TryAdd(address, dev))
                    {
                        dev.Dispose();
                        continue;
                    }
                    dev.ConnectionStatusChanged += OnHeldClassicConnectionChanged;
                    Upsert(address, dev.Name, -100, "Classic", true, false);
                    SetConnected(address, dev.ConnectionStatus == BluetoothConnectionStatus.Connected);
                }
            }
        }

        private void OnHeldLeConnectionChanged(BluetoothLEDevice sender, object args)
        {
            if (sender == null) return;
            var address = BluetoothDiscover.FormatFromUlong(sender.BluetoothAddress);
            var connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            SetConnected(address, connected);
            if (connected)
                Upsert(address, sender.Name, -100, "BLE", true, false);
            LogHelper.WriteLine($"BLE 连接变化: {address} {(connected ? "已连接" : "已断开")}");
        }

        private void OnHeldClassicConnectionChanged(BluetoothDevice sender, object args)
        {
            if (sender == null) return;
            var address = BluetoothDiscover.FormatFromUlong(sender.BluetoothAddress);
            var connected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected;
            SetConnected(address, connected);
            if (connected)
                Upsert(address, sender.Name, -100, "Classic", true, false);
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
            SetConnected(address, false);
            if (!_heldLe.TryRemove(address, out var dev) || dev == null) return;
            try { dev.ConnectionStatusChanged -= OnHeldLeConnectionChanged; } catch { }
            try { dev.Dispose(); } catch { }
        }

        private void ReleaseHeldClassic(string address)
        {
            SetConnected(address, false);
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
                (_, previous) =>
                {
                    var existing = previous.Copy();
                    if (!string.IsNullOrEmpty(name))
                        existing.Name = name;
                    if (rssi > -100)
                        existing.Rssi = rssi;
                    if (isPaired)
                        existing.IsPaired = true;
                    if (refreshSeen)
                        existing.LastSeen = DateTime.Now;
                    if (!string.IsNullOrEmpty(type) && (refreshSeen || existing.LastSeen == DateTime.MinValue))
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
                    if (EffectiveBluetoothType != 2)
                        LoadClassicPaired();
                    if (EffectiveBluetoothType != 1)
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
            Interlocked.Increment(ref _watcherGeneration);
            StopOneWatcher(ref _classicWatcher);
            StopOneWatcher(ref _leWatcher);

            StopAdvertisementWatcher();
        }

        private void StopAdvertisementWatcher()
        {
            var watcher = _advWatcher;
            _advWatcher = null;
            if (watcher == null) return;
            try
            {
                watcher.Received -= AdvWatcher_Received;
                if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) watcher.Stop();
            }
            catch { }
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
