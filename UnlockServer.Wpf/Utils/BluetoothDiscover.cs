using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace UnlockServer
{
    /// <summary>
    /// 蓝牙设备模型
    /// </summary>
    public class MybluetoothDevice
    {
        public string Name { get; set; }
        public string Address { get; set; }
        public short Rssi { get; set; }
        public string Type { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.MinValue;
        public bool IsPaired { get; set; }
        public bool IsInRange { get; set; }
        public bool HasRealRssi { get; set; }
    }

    /// <summary>
    /// RSSI 指数滑动平均，降低单次抖动。
    /// </summary>
    internal sealed class RssiSmoother
    {
        private readonly double _alpha;
        private double? _value;

        public RssiSmoother(double alpha = 0.35)
        {
            _alpha = alpha;
        }

        public short Update(short rssi)
        {
            if (_value == null)
                _value = rssi;
            else
                _value = _alpha * rssi + (1 - _alpha) * _value.Value;

            return (short)Math.Round(_value.Value);
        }

        public short? Value => _value.HasValue ? (short)Math.Round(_value.Value) : (short?)null;

        public void Reset()
        {
            _value = null;
        }
    }

    /// <summary>
    /// 近场存在检测：
    /// BLE 以广播 RSSI 为主（不主动连接，省电、数值真实）；
    /// 经典蓝牙以 DeviceWatcher 新 RSSI + 低频 Inquiry 为辅，不再用 RFCOMM 当主依据。
    /// LastSeen 只在收到「新观测」时刷新，避免 Windows 缓存 RSSI 导致关机后仍显示在附近。
    /// </summary>
    public class BluetoothDiscover
    {
        #region 常量

        private static readonly Regex MacRegex = new Regex(@"([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}", RegexOptions.Compiled);

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

        public const string BluetoothId = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")";
        public const string BluetoothLEId = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

        private const int RssiDeltaThreshold = 2;
        private const short FallbackInRangeRssi = -50;

        #endregion

        #region Windows Bluetooth Inquiry

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay;
            public ushort wHour, wMinute, wSecond, wMilliseconds;
        }

        #endregion

        #region 字段

        private DeviceWatcher _classicWatcher;
        private DeviceWatcher _leWatcher;
        private BluetoothLEAdvertisementWatcher _advWatcher;
        private BluetoothLEAdvertisementPublisher _advPublisher;
        private readonly ConcurrentDictionary<string, MybluetoothDevice> _devices;
        private readonly ConcurrentDictionary<string, short> _lastWatcherRssi = new ConcurrentDictionary<string, short>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, RssiSmoother> _smoothers = new ConcurrentDictionary<string, RssiSmoother>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _targets = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, BluetoothLEDevice> _heldLeDevices = new ConcurrentDictionary<string, BluetoothLEDevice>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _watcherConnected = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly int _bleType;
        private readonly object _scanModeLock = new object();

        private Timer _inquiryTimer;
        private Timer _staleTimer;
        private Timer _advRefreshTimer;
        private Timer _connectionStatusTimer;
        private Timer _lockScanTimer;
        private bool _isRunning;
        private int _presenceTimeoutSeconds = 8;
        private bool _usingActiveScan;
        private bool _sessionLocked;
        private int _intentionalStop;
        private DateTime _startedAt = DateTime.MinValue;
        private DateTime _lastAnyObservation = DateTime.MinValue;
        private DateTime _lastAdvertisement = DateTime.MinValue;
        private DateTime _lastWatcherRestart = DateTime.MinValue;
        private DateTime _holdStaleUntil = DateTime.MinValue;

        #endregion

        #region 事件

        /// <summary>地址, RSSI, 是否为真实广播/更新值</summary>
        public event Action<string, short, bool> OnRssiUpdated;

        public event Action<string, bool> OnDeviceStatusChanged;

        #endregion

        public BluetoothDiscover(int bletype = 0)
        {
            _bleType = (bletype == 1 || bletype == 2) ? bletype : 0;
            _devices = new ConcurrentDictionary<string, MybluetoothDevice>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>未见新广播后多少秒视为离开</summary>
        public int PresenceTimeoutSeconds
        {
            get => _presenceTimeoutSeconds;
            set => _presenceTimeoutSeconds = value < 3 ? 3 : (value > 60 ? 60 : value);
        }

        public void SetTargetAddress(string address)
        {
            SetTargetAddresses(new[] { address });
        }

        public void SetTargetAddresses(IEnumerable<string> addresses)
        {
            var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (addresses != null)
            {
                foreach (var raw in addresses)
                {
                    var n = NormalizeAddress(raw);
                    if (!string.IsNullOrEmpty(n))
                        next.Add(n);
                }
            }

            var same = next.SetEquals(_targets.Keys);
            _targets.Clear();
            foreach (var n in next)
                _targets[n] = 1;

            if (!same)
            {
                foreach (var key in _smoothers.Keys)
                {
                    if (!next.Contains(key))
                        _smoothers.TryRemove(key, out _);
                }
            }

            EnsureTargetPlaceholder();
            if (!same)
                LogHelper.WriteLine($"设置目标设备: {string.Join(", ", _targets.Keys)}");
        }

        private bool IsTarget(string address)
        {
            return !string.IsNullOrEmpty(address) && _targets.ContainsKey(address);
        }

        /// <summary>广播监听是否还在出数。为 false 时是扫描停了，不是设备离开。</summary>
        public bool IsAdvertisementAlive
        {
            get
            {
                if (_lastAdvertisement == DateTime.MinValue)
                    return _startedAt != DateTime.MinValue &&
                           (DateTime.Now - _startedAt).TotalSeconds < 20;
                return (DateTime.Now - _lastAdvertisement).TotalSeconds <= 15;
            }
        }

        /// <summary>最近是否还能听到任意蓝牙广播。锁屏后监听常会中断，此时不应把目标判为离开。</summary>
        public bool IsScanHealthy => IsAdvertisementAlive || HasConnectedTarget();

        public bool IsRefreshing => DateTime.Now < _holdStaleUntil;

        public bool IsTargetConnected(string address)
        {
            var n = NormalizeAddress(address);
            if (string.IsNullOrEmpty(n)) return false;
            if (IsHeldConnected(n)) return true;
            return _watcherConnected.TryGetValue(n, out var flag) && flag != 0;
        }

        /// <summary>扫描像是整表停了，而不是某台设备离开。</summary>
        public bool LooksLikeScanStall(DateTime targetLastSeen)
        {
            if (_sessionLocked || IsRefreshing)
                return true;
            if (!IsAdvertisementAlive && !HasConnectedTarget())
                return true;
            return false;
        }

        public int ScanSilenceSeconds
        {
            get
            {
                var from = _lastAnyObservation != DateTime.MinValue ? _lastAnyObservation : _startedAt;
                if (from == DateTime.MinValue) return int.MaxValue;
                return (int)(DateTime.Now - from).TotalSeconds;
            }
        }

        public void NotifySessionLocked()
        {
            _sessionLocked = true;
            LogHelper.WriteLine("会话已锁定，继续当前扫描，不重置设备状态");
            RestartInquiryTimer(5000);
            var watcherDead = _advWatcher == null ||
                (_advWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Started &&
                 _advWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Created);
            if (watcherDead)
                ScheduleRestart("session lock recovered");
            else if (!_usingActiveScan)
            {
                lock (_scanModeLock)
                    StartAdvertisementWatcher(active: true);
            }
            DisposeTimer(ref _lockScanTimer);
            _lockScanTimer = new Timer(LockScanKeepAlive, null, 4000, 8000);
        }

        public void NotifySessionUnlocked()
        {
            _sessionLocked = false;
            DisposeTimer(ref _lockScanTimer);
            RestartInquiryTimer(12000);
            ScheduleRestart("session unlock revive");
        }

        private void LockScanKeepAlive(object state)
        {
            if (!_isRunning || !_sessionLocked) return;

            var watcherDead = _advWatcher == null ||
                _advWatcher.Status != BluetoothLEAdvertisementWatcherStatus.Started;
            if (watcherDead || !IsAdvertisementAlive)
            {
                RefreshAdvertisementWatcher("lock keepalive");
                return;
            }

            if (!_usingActiveScan)
            {
                lock (_scanModeLock)
                    StartAdvertisementWatcher(active: true);
            }
        }

        private void RestartInquiryTimer(int periodMs)
        {
            DisposeTimer(ref _inquiryTimer);
            if (_isRunning)
                _inquiryTimer = new Timer(InquiryCallback, null, 1000, periodMs);
        }

        public void StartDiscover()
        {
            if (_isRunning) return;
            _isRunning = true;
            _startedAt = DateTime.Now;
            _lastAnyObservation = DateTime.MinValue;
            _lastAdvertisement = DateTime.MinValue;
            _holdStaleUntil = DateTime.MinValue;

            LoadPairedDevices();
            EnsureTargetPlaceholder();
            StartWatchers();

            if (_bleType != 1)
            {
                StartAdvertisementWatcher(active: true);
                StartScanKeepAlivePublisher();
            }

            _inquiryTimer = new Timer(InquiryCallback, null, 2000, 4000);
            _staleTimer = new Timer(StaleCallback, null, 2000, 2000);
            _advRefreshTimer = new Timer(AdvRefreshCallback, null, 20000, 20000);
            _connectionStatusTimer = new Timer(ConnectionStatusCallback, null, 2000, 3000);

            LogHelper.WriteLine($"存在检测已启动（模式 {(_bleType == 0 ? "全部" : _bleType == 1 ? "经典" : "BLE")}）");
        }

        public void StopDiscover()
        {
            _isRunning = false;

            DisposeTimer(ref _inquiryTimer);
            DisposeTimer(ref _staleTimer);
            DisposeTimer(ref _advRefreshTimer);
            DisposeTimer(ref _connectionStatusTimer);
            DisposeTimer(ref _lockScanTimer);

            StopScanKeepAlivePublisher();
            StopAdvertisementWatcher();
            StopWatchers();
            ReleaseHeldLeDevices();
            _watcherConnected.Clear();
            _devices.Clear();
            _lastWatcherRssi.Clear();
            _smoothers.Clear();
            LogHelper.WriteLine("蓝牙扫描已停止");
        }

        public List<MybluetoothDevice> getAllDevice()
        {
            return _devices.Values
                .OrderByDescending(d => d.IsPaired)
                .ThenByDescending(d => d.LastSeen)
                .ToList();
        }

        public short? GetDeviceRssi(string address)
        {
            var normalizedAddress = NormalizeAddress(address);
            if (string.IsNullOrEmpty(normalizedAddress)) return null;

            if (_devices.TryGetValue(normalizedAddress, out var device) && IsFresh(device))
                return device.Rssi;

            return null;
        }

        #region 已配对 / 占位

        private void EnsureTargetPlaceholder()
        {
            foreach (var address in _targets.Keys)
            {
                _devices.AddOrUpdate(address,
                    new MybluetoothDevice
                    {
                        Name = "",
                        Address = address,
                        Rssi = -100,
                        Type = "BLE",
                        IsPaired = true,
                        IsInRange = false,
                        LastSeen = DateTime.MinValue
                    },
                    (_, existing) => existing);
            }
        }

        private void LoadPairedDevices()
        {
            Task.Run(() =>
            {
                try
                {
                    if (_bleType != 2)
                        LoadClassicPaired();
                    if (_bleType != 1)
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
                var radio = BluetoothRadio.Default;
                if (radio == null) return;

                using (var client = new BluetoothClient())
                {
                    foreach (var device in client.PairedDevices)
                    {
                        var address = FormatMacAddress(device.DeviceAddress.ToString());
                        if (string.IsNullOrEmpty(address)) continue;

                        UpsertPaired(address, device.DeviceName, "Classic");
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"加载经典已配对设备失败: {ex.Message}");
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
                    var address = GetAddressFromAddEvent(info.Properties, info.Id);
                    if (string.IsNullOrEmpty(address)) continue;
                    UpsertPaired(address, info.Name, "BLE");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"加载 BLE 已配对设备失败: {ex.Message}");
            }
        }

        private void UpsertPaired(string address, string name, string type)
        {
            _devices.AddOrUpdate(address,
                new MybluetoothDevice
                {
                    Name = name ?? "",
                    Address = address,
                    Rssi = -100,
                    Type = type,
                    IsPaired = true,
                    IsInRange = false,
                    LastSeen = DateTime.MinValue
                },
                (_, existing) =>
                {
                    existing.IsPaired = true;
                    if (!string.IsNullOrEmpty(name) && string.IsNullOrEmpty(existing.Name))
                        existing.Name = name;
                    return existing;
                });

            LogHelper.WriteLine($"已配对设备: {name}[{address}]");
        }

        #endregion

        #region 广播扫描（BLE）

        private void StartAdvertisementWatcher(bool active)
        {
            try
            {
                StopAdvertisementWatcher();

                _advWatcher = new BluetoothLEAdvertisementWatcher
                {
                    ScanningMode = active ? BluetoothLEScanningMode.Active : BluetoothLEScanningMode.Passive
                };
                _advWatcher.Received += AdvWatcher_Received;
                _advWatcher.Stopped += AdvWatcher_Stopped;
                _advWatcher.Start();
                _usingActiveScan = active;
                LogHelper.WriteLine($"BLE 广播扫描: {(active ? "Active" : "Passive")}");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动 BLE 广播扫描失败: {ex.Message}");
            }
        }

        private void StopAdvertisementWatcher()
        {
            if (_advWatcher == null) return;
            Interlocked.Increment(ref _intentionalStop);
            try
            {
                _advWatcher.Received -= AdvWatcher_Received;
                _advWatcher.Stopped -= AdvWatcher_Stopped;
                if (_advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Started ||
                    _advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopping)
                {
                    _advWatcher.Stop();
                }
            }
            catch { }
            finally
            {
                _advWatcher = null;
                Interlocked.Decrement(ref _intentionalStop);
            }
        }

        private void AdvWatcher_Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            if (!_isRunning || _intentionalStop > 0) return;
            LogHelper.WriteLine($"BLE 广播扫描异常停止: {args.Error}");
            ScheduleRestart("adv watcher stopped");
        }

        private void AdvWatcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (!_isRunning) return;

            _lastAdvertisement = DateTime.Now;

            var address = FormatFromUlong(args.BluetoothAddress);
            if (string.IsNullOrEmpty(address)) return;

            var rssi = args.RawSignalStrengthInDBm;
            var name = args.Advertisement?.LocalName;

            ApplyObservation(address, rssi, true, name, "BLE");
        }

        private void AdvRefreshCallback(object state)
        {
            if (!_isRunning || _bleType == 1) return;
            if (HasConnectedTarget()) return;
            if (IsAdvertisementAlive) return;
            RefreshAdvertisementWatcher("ads silent");
        }

        private void StartScanKeepAlivePublisher()
        {
            try
            {
                StopScanKeepAlivePublisher();
                var writer = new DataWriter();
                writer.WriteBytes(new byte[] { 0x55, 0x53 });
                _advPublisher = new BluetoothLEAdvertisementPublisher();
                _advPublisher.Advertisement.ManufacturerData.Add(
                    new BluetoothLEManufacturerData(0xFFFF, writer.DetachBuffer()));
                _advPublisher.Start();
                LogHelper.WriteLine("已启动扫描保活广播");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"扫描保活广播不可用: {ex.Message}");
                _advPublisher = null;
            }
        }

        private void StopScanKeepAlivePublisher()
        {
            if (_advPublisher == null) return;
            try
            {
                if (_advPublisher.Status == BluetoothLEAdvertisementPublisherStatus.Started ||
                    _advPublisher.Status == BluetoothLEAdvertisementPublisherStatus.Waiting)
                {
                    _advPublisher.Stop();
                }
            }
            catch { }
            finally
            {
                _advPublisher = null;
            }
        }

        private void HoldTargetLeDevices()
        {
            if (_bleType == 1) return;
            Task.Run(async () =>
            {
                await Task.Delay(2500).ConfigureAwait(false);
                if (!_isRunning) return;
                foreach (var address in _targets.Keys)
                {
                    if (!_isRunning) return;
                    if (_heldLeDevices.ContainsKey(address)) continue;
                    try
                    {
                        var ul = ParseBluetoothAddress(address);
                        if (ul == 0) continue;
                        var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(ul);
                        if (dev == null || !_isRunning)
                        {
                            dev?.Dispose();
                            continue;
                        }
                        if (!_heldLeDevices.TryAdd(address, dev))
                        {
                            dev.Dispose();
                            continue;
                        }
                        dev.ConnectionStatusChanged += OnHeldConnectionStatusChanged;
                        LogHelper.WriteLine($"已保持 BLE 引用: {dev.Name}[{address}] {dev.ConnectionStatus}");
                        if (dev.ConnectionStatus == BluetoothConnectionStatus.Connected)
                            ApplyConnectedPresence(address, dev.Name, "BLE");
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLine($"保持 BLE 引用失败 {address}: {ex.Message}");
                    }
                }
            });
        }

        private void ReleaseHeldLeDevices()
        {
            foreach (var kv in _heldLeDevices)
            {
                try
                {
                    kv.Value.ConnectionStatusChanged -= OnHeldConnectionStatusChanged;
                    kv.Value.Dispose();
                }
                catch { }
            }
            _heldLeDevices.Clear();
        }

        private void OnHeldConnectionStatusChanged(BluetoothLEDevice sender, object args)
        {
            if (!_isRunning || sender == null) return;
            var address = FormatFromUlong(sender.BluetoothAddress);
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected)
                ApplyConnectedPresence(address, sender.Name, "BLE");
        }

        private bool HasConnectedTarget()
        {
            foreach (var address in _targets.Keys)
            {
                if (IsTargetConnected(address))
                    return true;
            }
            return false;
        }

        private bool IsHeldConnected(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;
            if (!_heldLeDevices.TryGetValue(address, out var dev) || dev == null)
                return false;
            try
            {
                return dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
            }
            catch
            {
                return false;
            }
        }

        private void ConnectionStatusCallback(object state)
        {
            if (!_isRunning) return;
            foreach (var address in _targets.Keys)
            {
                if (IsTargetConnected(address))
                    ApplyConnectedPresence(address, null, "BLE");
            }
        }

        private void SetWatcherConnected(string address, bool connected)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (connected)
                _watcherConnected[address] = 1;
            else
                _watcherConnected.TryRemove(address, out _);
        }

        private void RefreshAdvertisementWatcher(string reason)
        {
            if (!_isRunning || _bleType == 1) return;
            lock (_scanModeLock)
            {
                if ((DateTime.Now - _lastWatcherRestart).TotalSeconds < 8)
                    return;
                _lastWatcherRestart = DateTime.Now;
                _holdStaleUntil = DateTime.Now.AddSeconds(5);
                LogHelper.WriteLine($"刷新 BLE 广播扫描: {reason}");
                StartAdvertisementWatcher(active: true);
            }
        }

        #endregion

        #region DeviceWatcher

        private void StartWatchers()
        {
            if (_bleType != 2)
                _classicWatcher = CreateWatcher(BluetoothId, "Classic");
            if (_bleType != 1)
                _leWatcher = CreateWatcher(BluetoothLEId, "BLE");
        }

        private DeviceWatcher CreateWatcher(string selector, string type)
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(
                    selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
                watcher.Added += (s, info) => OnWatcherAdded(info, type);
                watcher.Updated += (s, update) => OnWatcherUpdated(update, type);
                watcher.Removed += Watcher_Removed;
                watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
                watcher.Stopped += Watcher_Stopped;
                watcher.Start();
                return watcher;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动 {type} DeviceWatcher 失败: {ex.Message}");
                return null;
            }
        }

        private void StopWatchers()
        {
            Interlocked.Increment(ref _intentionalStop);
            try
            {
                StopOneWatcher(ref _classicWatcher);
                StopOneWatcher(ref _leWatcher);
            }
            finally
            {
                Interlocked.Decrement(ref _intentionalStop);
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

        private void OnWatcherAdded(DeviceInformation deviceInfo, string type)
        {
            var address = GetAddressFromAddEvent(deviceInfo.Properties, deviceInfo.Id);
            if (string.IsNullOrEmpty(address)) return;

            var isPaired = GetIsPaired(deviceInfo.Properties);
            var name = deviceInfo.Name;

            _devices.AddOrUpdate(address,
                new MybluetoothDevice
                {
                    Name = name ?? "",
                    Address = address,
                    Rssi = -100,
                    Type = type,
                    IsPaired = isPaired,
                    IsInRange = false,
                    LastSeen = DateTime.MinValue
                },
                (_, existing) =>
                {
                    if (!string.IsNullOrEmpty(name))
                        existing.Name = name;
                    if (isPaired)
                        existing.IsPaired = true;
                    if (!string.IsNullOrEmpty(type))
                        existing.Type = type;
                    return existing;
                });

            if (HasBool(deviceInfo.Properties, IsConnectedProperty))
            {
                var connected = GetIsConnected(deviceInfo.Properties);
                SetWatcherConnected(address, connected);
                if (connected)
                    ApplyConnectedPresence(address, name, type);
            }

            if (TryGetRssi(deviceInfo.Properties, out short rssi))
                TryAcceptWatcherRssi(address, rssi, name, type, forceTarget: true);
        }

        private void OnWatcherUpdated(DeviceInformationUpdate update, string type)
        {
            var address = GetAddressFromAddEvent(update.Properties, update.Id);
            if (string.IsNullOrEmpty(address)) return;

            if (HasBool(update.Properties, IsConnectedProperty))
            {
                var connected = GetIsConnected(update.Properties);
                SetWatcherConnected(address, connected);
                if (connected)
                    ApplyConnectedPresence(address, null, type);
            }

            if (TryGetRssi(update.Properties, out short rssi))
                TryAcceptWatcherRssi(address, rssi, null, type, forceTarget: true);
        }

        private void Watcher_Removed(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            // Windows 会周期性触发 Removed，不代表设备真的离开，忽略。
        }

        private void Watcher_EnumerationCompleted(DeviceWatcher watcher, object _)
        {
            LogHelper.WriteLine($"蓝牙枚举完成，缓存 {_devices.Count} 个设备");
        }

        private void Watcher_Stopped(DeviceWatcher watcher, object _)
        {
            LogHelper.WriteLine("DeviceWatcher 已停止");
        }

        private void ScheduleRestart(string reason)
        {
            lock (_scanModeLock)
            {
                if (!_isRunning) return;
                var minGap = _sessionLocked ? 2 : 4;
                if (_lastWatcherRestart != DateTime.MinValue &&
                    (DateTime.Now - _lastWatcherRestart).TotalSeconds < minGap)
                    return;
                _lastWatcherRestart = DateTime.Now;
            }

            LogHelper.WriteLine($"重启蓝牙扫描: {reason}");
            try
            {
                StopAdvertisementWatcher();
                StopWatchers();
                StartWatchers();
                if (_bleType != 1)
                    StartAdvertisementWatcher(active: true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"重启蓝牙扫描失败: {ex.Message}");
            }
        }

        private void TryAcceptWatcherRssi(string address, short rssi, string name, string type, bool forceTarget = false)
        {
            if (_lastWatcherRssi.TryGetValue(address, out var prev) && prev == rssi)
            {
                if (IsTarget(address) && (forceTarget || IsTargetConnected(address)))
                    ApplyConnectedPresence(address, name, type);
                return;
            }

            _lastWatcherRssi[address] = rssi;
            ApplyObservation(address, rssi, true, name, type);
        }

        #endregion

        #region 定时器：Inquiry / 连接状态 / 新鲜度

        private void InquiryCallback(object state)
        {
            if (!_isRunning || _targets.IsEmpty) return;

            Task.Run(() =>
            {
                try
                {
                    foreach (var target in _targets.Keys)
                    {
                        if (InquireTargetPresent(target, out var name, out var connected) && connected)
                        {
                            SetWatcherConnected(target, true);
                            ApplyConnectedPresence(target, name, "Classic");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLine($"Inquiry 失败: {ex.Message}");
                }
            });
        }

        private bool InquireTargetPresent(string target, out string name, out bool connected)
        {
            name = null;
            connected = false;

            var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS)),
                fReturnAuthenticated = true,
                fReturnRemembered = true,
                fReturnUnknown = true,
                fReturnConnected = true,
                fIssueInquiry = false,
                cTimeoutMultiplier = 1,
                hRadio = IntPtr.Zero
            };

            var info = new BLUETOOTH_DEVICE_INFO
            {
                dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO))
            };

            var handle = BluetoothFindFirstDevice(ref search, ref info);
            if (handle == IntPtr.Zero) return false;

            try
            {
                do
                {
                    var address = FormatFromUlong(info.Address);
                    if (!address.Equals(target, StringComparison.OrdinalIgnoreCase))
                        continue;

                    name = info.szName;
                    connected = info.fConnected;
                    return true;
                } while (BluetoothFindNextDevice(handle, ref info));
            }
            finally
            {
                BluetoothFindDeviceClose(handle);
            }

            return false;
        }

        private void StaleCallback(object state)
        {
            if (!_isRunning) return;
            if (DateTime.Now < _holdStaleUntil) return;
            if (_sessionLocked) return;

            if (_advWatcher == null ||
                _advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted ||
                _advWatcher.Status == BluetoothLEAdvertisementWatcherStatus.Stopped ||
                !IsAdvertisementAlive)
            {
                RefreshAdvertisementWatcher(_advWatcher == null ? "watcher missing" : "ads silent");
                return;
            }

            foreach (var device in _devices.Values)
            {
                if (!IsTarget(device.Address)) continue;
                if (!device.IsInRange) continue;
                if (IsFresh(device) || IsTargetConnected(device.Address)) continue;
                if (LooksLikeScanStall(device.LastSeen))
                    continue;

                SetInRange(device, false);
                device.Rssi = -100;
                OnRssiUpdated?.Invoke(device.Address, -100, false);
            }
        }

        #endregion

        #region 观测合并

        private void ApplyObservation(string address, short rssi, bool isRealRssi, string name, string type)
        {
            if (string.IsNullOrEmpty(address)) return;

            _lastAnyObservation = DateTime.Now;

            var reportRssi = rssi;
            if (IsTarget(address) && isRealRssi)
            {
                var smoother = _smoothers.GetOrAdd(address, _ => new RssiSmoother());
                reportRssi = smoother.Update(rssi);
            }

            var isNew = !_devices.ContainsKey(address);
            var device = _devices.AddOrUpdate(address,
                new MybluetoothDevice
                {
                    Name = name ?? "",
                    Address = address,
                    Rssi = reportRssi,
                    Type = type,
                    HasRealRssi = isRealRssi,
                    LastSeen = DateTime.Now,
                    IsInRange = true
                },
                (_, existing) =>
                {
                    if (!string.IsNullOrEmpty(name))
                        existing.Name = name;
                    if (isRealRssi || !existing.HasRealRssi)
                    {
                        if (Math.Abs(existing.Rssi - reportRssi) >= RssiDeltaThreshold || existing.Rssi <= -100)
                            existing.Rssi = reportRssi;
                        existing.HasRealRssi = existing.HasRealRssi || isRealRssi;
                    }
                    existing.LastSeen = DateTime.Now;
                    existing.Type = type;
                    return existing;
                });

            if (IsTarget(address))
            {
                SetInRange(device, true);
                OnRssiUpdated?.Invoke(address, device.Rssi, device.HasRealRssi);
                if (isNew)
                    LogHelper.WriteLine($"发现目标设备: {device.Name}[{address}] {reportRssi}dBm");
            }
            else
            {
                device.IsInRange = true;
            }
        }

        private void ApplyConnectedPresence(string address, string name, string type = null)
        {
            if (string.IsNullOrEmpty(address)) return;

            if (_devices.TryGetValue(address, out var device) && device.HasRealRssi && IsFresh(device))
            {
                device.LastSeen = DateTime.Now;
                SetInRange(device, true);
                return;
            }

            ApplyObservation(address, FallbackInRangeRssi, false, name, type ?? "BLE");
        }

        private void SetInRange(MybluetoothDevice device, bool inRange)
        {
            if (device.IsInRange == inRange) return;
            device.IsInRange = inRange;
            OnDeviceStatusChanged?.Invoke(device.Address, inRange);
            LogHelper.WriteLine($"设备 {device.Address} 状态: {(inRange ? "在范围内" : "不在范围内")}");
        }

        private bool IsFresh(MybluetoothDevice device)
        {
            if (device == null) return false;
            if (IsTargetConnected(device.Address)) return true;
            if (device.LastSeen == DateTime.MinValue) return false;
            return (DateTime.Now - device.LastSeen).TotalSeconds <= _presenceTimeoutSeconds;
        }

        #endregion

        #region 辅助

        private static void DisposeTimer(ref Timer timer)
        {
            timer?.Dispose();
            timer = null;
        }

        public static string NormalizeAddress(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var bracketStart = s.LastIndexOf('[');
            var bracketEnd = s.LastIndexOf(']');
            if (bracketStart >= 0 && bracketEnd > bracketStart)
                s = s.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);

            var matches = MacRegex.Matches(s);
            if (matches.Count > 0)
                return matches[matches.Count - 1].Value.ToUpperInvariant();

            var hex = Regex.Replace(s, "[^0-9A-Fa-f]", "");
            if (hex.Length == 12)
            {
                return string.Join(":",
                    Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToUpperInvariant();
            }

            var idx = s.LastIndexOf('-');
            if (idx >= 0 && idx + 1 < s.Length)
                return s.Substring(idx + 1).Trim().ToUpperInvariant();

            return s.Trim().ToUpperInvariant();
        }

        public static string FormatMacAddress(string address)
        {
            return NormalizeAddress(address);
        }

        public static string FormatFromUlong(ulong address)
        {
            var bytes = BitConverter.GetBytes(address);
            return string.Join(":", Enumerable.Range(0, 6).Select(i => bytes[i].ToString("X2")).Reverse());
        }

        public static ulong ParseBluetoothAddress(string address)
        {
            try
            {
                var hex = Regex.Replace(address ?? "", "[^0-9A-Fa-f]", "");
                if (hex.Length != 12) return 0;
                return Convert.ToUInt64(hex, 16);
            }
            catch
            {
                return 0;
            }
        }

        private static string GetAddressFromAddEvent(IReadOnlyDictionary<string, object> props, string idFallback)
        {
            string raw = null;
            if (props != null && props.TryGetValue(DeviceAddressProperty, out var addrObj))
                raw = addrObj?.ToString();

            var norm = NormalizeAddress(raw);
            if (!string.IsNullOrEmpty(norm)) return norm;
            return NormalizeAddress(idFallback);
        }

        private static bool TryGetRssi(IReadOnlyDictionary<string, object> props, out short rssi)
        {
            rssi = 0;
            if (props == null) return false;
            if (!props.TryGetValue(SignalStrengthProperty, out var val) || val == null) return false;

            try
            {
                var n = Convert.ToInt32(val);
                if (n < short.MinValue) n = short.MinValue;
                if (n > short.MaxValue) n = short.MaxValue;
                rssi = (short)n;
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

        #endregion
    }
}
