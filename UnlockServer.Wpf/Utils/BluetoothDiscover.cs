using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using InTheHand.Net;
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
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public bool IsPaired { get; set; }
        public bool IsInRange { get; set; }
    }

    /// <summary>
    /// 蓝牙设备发现类（增强版）
    /// 功能：
    /// 1. 支持经典蓝牙和 BLE 设备发现
    /// 2. 支持已配对设备的主动探测
    /// 3. 自动重扫描机制
    /// 4. 设备缓存管理
    /// </summary>
    public class BluetoothDiscover
    {
        #region 常量

        private static readonly Regex MacRegex = new Regex(@"([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}", RegexOptions.Compiled);

        private static readonly string[] RequestedProperties = new[]
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

        public const string BluetoothId = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")";
        public const string BluetoothLEId = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

        private const int RssiDeltaThreshold = 2;
        private const int DeviceTimeoutSeconds = 30;
        private const int RescanIntervalMs = 15000;
        
        // 主动探测的 RSSI 模拟值
        // 注意：RSSI_IN_RANGE 必须高于用户设置的阈值（默认 -55），否则会被判定为不在范围
        private const short RSSI_IN_RANGE = -40;      // 设备在范围内（模拟值，表示很近）
        private const short RSSI_OUT_OF_RANGE = -100; // 设备不在范围内

        #endregion

        #region Windows Bluetooth API

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern int BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnAuthenticated;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnRemembered;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnUnknown;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fReturnConnected;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fIssueInquiry;      // 关键：执行真正的蓝牙查询
            public byte cTimeoutMultiplier; // 查询超时倍数 (1.28秒 * n)
            public IntPtr hRadio;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fConnected;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fRemembered;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear;
            public ushort wMonth;
            public ushort wDayOfWeek;
            public ushort wDay;
            public ushort wHour;
            public ushort wMinute;
            public ushort wSecond;
            public ushort wMilliseconds;
        }

        #endregion

        #region 字段

        private DeviceWatcher _watcher;
        private readonly ConcurrentDictionary<string, MybluetoothDevice> _devices;
        private readonly int _bleType;
        private Timer _rescanTimer;
        private Timer _cleanupTimer;
        private Timer _connectionCheckTimer;  // 连接状态检测定时器
        private bool _isRunning;
        private string _targetAddress;
        
        // 持久连接相关
        private BluetoothClient _persistentClient;
        private bool _isPersistentConnected;
        private DateTime _lastConnectionCheck = DateTime.MinValue;
        private int _connectionFailCount = 0;
        private int _connectionSuccessCount = 0;
        private const int FailCountToDisconnect = 2;  // 连续2次失败就判定离开（更灵敏）
        private const int SuccessCountToConnect = 1;   // 1次成功就判定在范围
        
        // 状态稳定性
        private bool _lastReportedStatus = true;  // 初始假设在范围内，这样第一次失败检测会触发更新
        private bool _hasInitialStatus = false;   // 是否已经有初始状态
        private DateTime _statusChangeTime = DateTime.MinValue;

        #endregion

        #region 事件

        /// <summary>
        /// 设备 RSSI 更新事件
        /// 参数：地址, RSSI值, 是否为真实值
        /// </summary>
        public event Action<string, short, bool> OnRssiUpdated;

        /// <summary>
        /// 设备状态变化事件（在范围/不在范围）
        /// </summary>
        public event Action<string, bool> OnDeviceStatusChanged;

        #endregion

        #region 构造函数

        public BluetoothDiscover(int bletype = 1)
        {
            _bleType = bletype == 2 ? 2 : 1;
            _devices = new ConcurrentDictionary<string, MybluetoothDevice>(StringComparer.OrdinalIgnoreCase);

            var protocolId = _bleType == 1 ? BluetoothId : BluetoothLEId;
            _watcher = DeviceInformation.CreateWatcher(
                protocolId, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置目标设备地址（用于主动探测）
        /// </summary>
        public void SetTargetAddress(string address)
        {
            _targetAddress = NormalizeAddress(address);
            // 重置状态
            _hasInitialStatus = false;
            _connectionFailCount = 0;
            _connectionSuccessCount = 0;
            LogHelper.WriteLine($"设置目标设备地址: {_targetAddress}");
        }

        public void StartDiscover()
        {
            if (_isRunning) return;
            _isRunning = true;

            // 先加载已配对设备
            LoadPairedDevices();

            // 启动 DeviceWatcher
            StartWatcher();

            // 启动定时重扫描（每15秒重启一次 watcher）
            _rescanTimer = new Timer(RescanCallback, null, RescanIntervalMs, RescanIntervalMs);

            // 启动设备清理定时器（每10秒清理超时设备）
            _cleanupTimer = new Timer(CleanupCallback, null, 10000, 10000);

            // 只使用一个连接状态检测定时器（每3秒检测一次，更灵敏）
            // 移除 _probeTimer，避免多个定时器同时探测导致冲突
            _connectionCheckTimer = new Timer(CheckPersistentConnection, null, 3000, 3000);

            LogHelper.WriteLine($"{(_bleType == 1 ? "经典蓝牙" : "BLE")}扫描已启动");
        }

        public void StopDiscover()
        {
            _isRunning = false;

            _rescanTimer?.Dispose();
            _rescanTimer = null;

            _cleanupTimer?.Dispose();
            _cleanupTimer = null;

            _connectionCheckTimer?.Dispose();
            _connectionCheckTimer = null;
            
            // 关闭持久连接
            ClosePersistentConnection();

            StopWatcher();

            _devices.Clear();
            LogHelper.WriteLine("蓝牙扫描已停止");
        }

        public List<MybluetoothDevice> getAllDevice()
        {
            return _devices.Values
                .OrderByDescending(d => d.IsPaired)
                .ThenByDescending(d => d.LastSeen)
                .ToList();
        }

        /// <summary>
        /// 获取指定地址设备的 RSSI
        /// </summary>
        public short? GetDeviceRssi(string address)
        {
            var normalizedAddress = NormalizeAddress(address);
            if (string.IsNullOrEmpty(normalizedAddress)) return null;

            if (_devices.TryGetValue(normalizedAddress, out var device))
            {
                if ((DateTime.Now - device.LastSeen).TotalSeconds < DeviceTimeoutSeconds)
                {
                    return device.Rssi;
                }
            }

            return null;
        }

        #endregion

        #region 已配对设备处理

        private void LoadPairedDevices()
        {
            if (_bleType != 1) return; // 只对经典蓝牙处理

            try
            {
                // 获取本地蓝牙适配器的已配对设备
                var radio = BluetoothRadio.Default;
                if (radio == null) return;

                var client = new BluetoothClient();
                // 获取已记住（配对）的设备
                var devices = client.PairedDevices;

                foreach (var device in devices)
                {
                    try
                    {
                        var address = FormatMacAddress(device.DeviceAddress.ToString());
                        if (string.IsNullOrEmpty(address)) continue;

                        var btDevice = new MybluetoothDevice
                        {
                            Name = device.DeviceName ?? "",
                            Address = address,
                            Rssi = RSSI_OUT_OF_RANGE,
                            Type = "Classic",
                            IsPaired = true,
                            IsInRange = false,
                            LastSeen = DateTime.Now
                        };

                        _devices.AddOrUpdate(address, btDevice, (_, existing) =>
                        {
                            existing.IsPaired = true;
                            if (string.IsNullOrEmpty(existing.Name))
                                existing.Name = btDevice.Name;
                            return existing;
                        });

                        LogHelper.WriteLine($"发现已配对设备: {btDevice.Name}[{address}]");
                    }
                    catch { }
                }

                client.Close();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"加载已配对设备失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 主动探测目标设备是否在范围内
        /// </summary>
        private void ProbeTargetDevice(object state)
        {
            if (!_isRunning || string.IsNullOrEmpty(_targetAddress)) return;
            if (_bleType != 1) return; // 只对经典蓝牙有效

            Task.Run(() =>
            {
                try
                {
                    var isInRange = ProbeDeviceByAddress(_targetAddress);
                    
                    // 更新设备状态
                    if (_devices.TryGetValue(_targetAddress, out var device))
                    {
                        var previousState = device.IsInRange;
                        device.IsInRange = isInRange;
                        device.LastSeen = DateTime.Now;

                        if (isInRange)
                        {
                            // 如果 DeviceWatcher 没有提供真实 RSSI，使用模拟值
                            if (device.Rssi <= RSSI_OUT_OF_RANGE)
                            {
                                device.Rssi = RSSI_IN_RANGE;
                            }
                        }
                        else
                        {
                            device.Rssi = RSSI_OUT_OF_RANGE;
                        }

                        // 通知 RSSI 更新（模拟值，非真实RSSI）
                        OnRssiUpdated?.Invoke(_targetAddress, device.Rssi, false);

                        // 状态变化时通知
                        if (previousState != isInRange)
                        {
                            OnDeviceStatusChanged?.Invoke(_targetAddress, isInRange);
                            LogHelper.WriteLine($"设备 {_targetAddress} 状态变化: {(isInRange ? "在范围内" : "不在范围内")}");
                        }
                    }
                    else
                    {
                        // 设备不在列表中，尝试添加
                        if (isInRange)
                        {
                            var newDevice = new MybluetoothDevice
                            {
                                Name = "",
                                Address = _targetAddress,
                                Rssi = RSSI_IN_RANGE,
                                Type = "Classic",
                                IsPaired = true,
                                IsInRange = true,
                                LastSeen = DateTime.Now
                            };
                            _devices.TryAdd(_targetAddress, newDevice);
                            OnRssiUpdated?.Invoke(_targetAddress, RSSI_IN_RANGE, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLine($"探测设备失败: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 通过蓝牙地址探测设备是否在范围内
        /// 不使用系统缓存，只通过实际连接测试
        /// </summary>
        private bool ProbeDeviceByAddress(string address)
        {
            try
            {
                var btAddress = ParseBluetoothAddress(address);
                if (btAddress == 0) return false;

                var deviceAddress = new BluetoothAddress(btAddress);
                
                // 直接进行连接测试（不使用 Refresh/Remembered，那些会用缓存）
                return TryConnect(deviceAddress);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"探测设备 {address} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 尝试通过蓝牙连接检测设备
        /// </summary>
        private bool TryConnect(BluetoothAddress address)
        {
            BluetoothClient client = null;
            bool result = false;
            
            try
            {
                client = new BluetoothClient();
                var endpoint = new BluetoothEndPoint(address, BluetoothService.SerialPort);
                
                var ar = client.BeginConnect(endpoint, null, null);
                bool completed = ar.AsyncWaitHandle.WaitOne(3000); // 3秒超时
                
                if (completed)
                {
                    try
                    {
                        client.EndConnect(ar);
                        result = true;
                        LogHelper.WriteLine($"探测: {_targetAddress} 连接成功（在范围内）");
                    }
                    catch (SocketException ex)
                    {
                        // 只有 10061（连接被拒绝）才表示设备在范围内
                        // 其他错误（如 10060 超时、10065 不可达）都表示不在范围
                        if (ex.ErrorCode == 10061)
                        {
                            result = true;
                            LogHelper.WriteLine($"探测: {_targetAddress} 拒绝连接（在范围内）");
                        }
                        else
                        {
                            result = false;
                            LogHelper.WriteLine($"探测: {_targetAddress} 错误码 {ex.ErrorCode}（不在范围）");
                        }
                    }
                    catch
                    {
                        result = false;
                    }
                }
                else
                {
                    // 超时 = 设备不在范围内
                    result = false;
                    LogHelper.WriteLine($"探测: {_targetAddress} 超时（不在范围）");
                }
            }
            catch (SocketException ex)
            {
                // 只有 10061 才表示在范围内
                result = (ex.ErrorCode == 10061);
            }
            catch
            {
                result = false;
            }
            finally
            {
                try { client?.Close(); client?.Dispose(); } catch { }
            }
            
            return result;
        }

        /// <summary>
        /// 解析蓝牙地址为 ulong
        /// </summary>
        private static ulong ParseBluetoothAddress(string address)
        {
            try
            {
                // 移除所有分隔符
                var hex = Regex.Replace(address ?? "", "[^0-9A-Fa-f]", "");
                if (hex.Length != 12) return 0;

                return Convert.ToUInt64(hex, 16);
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region 持久连接检测（类似微软动态锁定）

        /// <summary>
        /// 检测持久连接状态
        /// </summary>
        private void CheckPersistentConnection(object state)
        {
            if (!_isRunning || string.IsNullOrEmpty(_targetAddress)) return;
            if (_bleType != 1) return; // 只对经典蓝牙有效

            Task.Run(() =>
            {
                try
                {
                    LogHelper.WriteLine($"开始检测设备 {_targetAddress}...");
                    
                    // 使用 Bluetooth Inquiry 检测设备是否在范围内
                    // 注意：不依赖 DeviceWatcher 的 RSSI，因为那可能是 Windows 缓存的旧值
                    var isConnected = CheckOrEstablishConnection();
                    LogHelper.WriteLine($"连接检测结果: {(isConnected ? "成功" : "失败")}");
                    
                    if (isConnected)
                    {
                        _connectionSuccessCount++;
                        _connectionFailCount = 0;
                        
                        // 首次或状态变化时更新
                        if (!_hasInitialStatus || !_lastReportedStatus)
                        {
                            _hasInitialStatus = true;
                            _isPersistentConnected = true;
                            _lastReportedStatus = true;
                            LogHelper.WriteLine($"状态更新: 在范围内");
                            UpdateDeviceStatus(_targetAddress, true, RSSI_IN_RANGE);
                        }
                    }
                    else
                    {
                        _connectionFailCount++;
                        _connectionSuccessCount = 0;
                        
                        LogHelper.WriteLine($"失败计数: {_connectionFailCount}/{FailCountToDisconnect}");
                        
                        // 连续失败多次才判定为离开
                        if (_connectionFailCount >= FailCountToDisconnect)
                        {
                            // 首次或状态变化时更新
                            if (!_hasInitialStatus || _lastReportedStatus)
                            {
                                _hasInitialStatus = true;
                                _isPersistentConnected = false;
                                _lastReportedStatus = false;
                                LogHelper.WriteLine($"状态更新: 不在范围内");
                                UpdateDeviceStatus(_targetAddress, false, RSSI_OUT_OF_RANGE);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLine($"连接状态检测异常: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 检查或建立蓝牙连接
        /// </summary>
        private bool CheckOrEstablishConnection()
        {
            try
            {
                var btAddress = ParseBluetoothAddress(_targetAddress);
                if (btAddress == 0) return false;

                var deviceAddress = new BluetoothAddress(btAddress);

                // 直接尝试连接测试（不依赖系统缓存）
                return QuickConnectionTest(deviceAddress);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"连接检测异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检测设备是否在范围内
        /// 只使用 RFCOMM 连接测试（不使用 Inquiry，因为 Inquiry 会返回已配对设备的缓存信息）
        /// </summary>
        private bool QuickConnectionTest(BluetoothAddress address)
        {
            BluetoothClient testClient = null;
            IAsyncResult ar = null;
            
            try
            {
                // 先检查本机蓝牙是否开启
                var radio = BluetoothRadio.Default;
                if (radio == null || radio.Mode == RadioMode.PowerOff)
                {
                    LogHelper.WriteLine($"检测: 本机蓝牙已关闭");
                    return false;
                }
                
                LogHelper.WriteLine($"检测: 连接测试 {_targetAddress}...");
                
                testClient = new BluetoothClient();
                var endpoint = new BluetoothEndPoint(address, BluetoothService.SerialPort);
                
                ar = testClient.BeginConnect(endpoint, null, null);
                // 等待 2 秒
                bool completed = ar.AsyncWaitHandle.WaitOne(2000);
                
                if (completed)
                {
                    try
                    {
                        testClient.EndConnect(ar);
                        // 连接成功 = 设备在范围内
                        LogHelper.WriteLine($"  → 连接成功 ✓");
                        return true;
                    }
                    catch (SocketException ex)
                    {
                        LogHelper.WriteLine($"  → 错误码: {ex.ErrorCode}");
                        // 10061: 连接被拒绝 - 设备响应了（在范围内）
                        // 10048: 地址已在使用 - 上次连接未完全关闭，忽略此次
                        if (ex.ErrorCode == 10061)
                        {
                            LogHelper.WriteLine($"  → 连接被拒绝(设备响应) ✓");
                            return true;
                        }
                        if (ex.ErrorCode == 10048)
                        {
                            // 端口被占用，等待后重试
                            LogHelper.WriteLine($"  → 端口占用，跳过此次检测");
                            return false; // 不算作失败，保持之前状态
                        }
                        LogHelper.WriteLine($"  → 设备不在范围 ✗");
                        return false;
                    }
                }
                else
                {
                    // 超时 = 设备不在范围
                    LogHelper.WriteLine($"  → 连接超时 ✗");
                    return false;
                }
            }
            catch (SocketException ex)
            {
                if (ex.ErrorCode == 10048)
                {
                    LogHelper.WriteLine($"  → 端口占用，跳过此次检测");
                    return false;
                }
                LogHelper.WriteLine($"  → 连接异常: {ex.ErrorCode} - {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"检测失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 确保正确关闭连接
                try
                {
                    if (testClient != null)
                    {
                        if (testClient.Connected)
                        {
                            testClient.GetStream()?.Close();
                        }
                        testClient.Close();
                        testClient.Dispose();
                    }
                }
                catch { }
                
                // 释放异步句柄
                try { ar?.AsyncWaitHandle?.Close(); } catch { }
            }
        }

        /// <summary>
        /// 更新设备状态
        /// </summary>
        private void UpdateDeviceStatus(string address, bool isInRange, short rssi)
        {
            if (_devices.TryGetValue(address, out var device))
            {
                var previousState = device.IsInRange;
                device.IsInRange = isInRange;
                device.LastSeen = DateTime.Now;
                
                // 当设备不在范围时，强制更新 RSSI 为模拟值
                // 当设备在范围时，只有没有真实 RSSI 时才使用模拟值
                if (!isInRange)
                {
                    // 设备不在范围，强制使用模拟值
                    device.Rssi = rssi;
                }
                else if (device.Rssi == RSSI_OUT_OF_RANGE || device.Rssi == RSSI_IN_RANGE)
                {
                    // 设备在范围，但没有真实 RSSI，使用模拟值
                    device.Rssi = rssi;
                }
                
                // 模拟值，非真实RSSI
                OnRssiUpdated?.Invoke(address, device.Rssi, false);
                
                if (previousState != isInRange)
                {
                    OnDeviceStatusChanged?.Invoke(address, isInRange);
                }
            }
            else if (isInRange)
            {
                // 设备不在列表但检测到在范围内
                var newDevice = new MybluetoothDevice
                {
                    Name = "",
                    Address = address,
                    Rssi = rssi,
                    Type = "Classic",
                    IsPaired = true,
                    IsInRange = true,
                    LastSeen = DateTime.Now
                };
                _devices.TryAdd(address, newDevice);
                OnRssiUpdated?.Invoke(address, rssi, false);
                OnDeviceStatusChanged?.Invoke(address, true);
            }
        }

        /// <summary>
        /// 关闭持久连接
        /// </summary>
        private void ClosePersistentConnection()
        {
            try
            {
                _persistentClient?.Close();
            }
            catch { }
            finally
            {
                _persistentClient = null;
                _isPersistentConnected = false;
            }
        }

        #endregion

        #region DeviceWatcher 处理

        private void StartWatcher()
        {
            try
            {
                if (_watcher.Status == DeviceWatcherStatus.Created ||
                    _watcher.Status == DeviceWatcherStatus.Stopped ||
                    _watcher.Status == DeviceWatcherStatus.Aborted)
                {
                    HookWatcher(_watcher);
                    _watcher.Start();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动 DeviceWatcher 失败: {ex.Message}");
            }
        }

        private void StopWatcher()
        {
            try
            {
                UnhookWatcher(_watcher);
                if (_watcher.Status == DeviceWatcherStatus.Started ||
                    _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _watcher.Stop();
                }
            }
            catch { }
        }

        private void RestartWatcher()
        {
            try
            {
                StopWatcher();
                Thread.Sleep(500);

                var protocolId = _bleType == 1 ? BluetoothId : BluetoothLEId;
                _watcher = DeviceInformation.CreateWatcher(
                    protocolId, RequestedProperties, DeviceInformationKind.AssociationEndpoint);

                StartWatcher();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"重启 DeviceWatcher 失败: {ex.Message}");
            }
        }

        private void HookWatcher(DeviceWatcher watcher)
        {
            watcher.Added += Watcher_Added;
            watcher.Updated += Watcher_Updated;
            watcher.Removed += Watcher_Removed;
            watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
            watcher.Stopped += Watcher_Stopped;
        }

        private void UnhookWatcher(DeviceWatcher watcher)
        {
            try
            {
                watcher.Added -= Watcher_Added;
                watcher.Updated -= Watcher_Updated;
                watcher.Removed -= Watcher_Removed;
                watcher.EnumerationCompleted -= Watcher_EnumerationCompleted;
                watcher.Stopped -= Watcher_Stopped;
            }
            catch { }
        }

        private void Watcher_Added(DeviceWatcher watcher, DeviceInformation deviceInfo)
        {
            TryGetRssi(deviceInfo.Properties, out short rssi);

            var address = GetAddressFromAddEvent(deviceInfo.Properties, deviceInfo.Id);
            if (string.IsNullOrEmpty(address)) return;

            var isPaired = GetIsPaired(deviceInfo.Properties);

            var device = new MybluetoothDevice
            {
                Name = deviceInfo.Name ?? "",
                Address = address,
                Rssi = rssi != 0 ? rssi : (short)-100,
                Type = _bleType == 1 ? "Classic" : "BLE",
                IsPaired = isPaired,
                IsInRange = false,  // 默认不在范围，由连接测试确认
                LastSeen = DateTime.Now
            };

            UpsertDevice(address, device, rssi);

            // 注意：DeviceWatcher 返回的 RSSI 可能是 Windows 缓存的旧值
            // 特别是对于已配对设备，即使设备不在范围，也会返回之前缓存的 RSSI
            // 因此不能将 DeviceWatcher 的 RSSI 当作"真实 RSSI"
            // 只有连接测试成功才能确认设备真正在范围内
        }

        private void Watcher_Updated(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            if (!TryGetRssi(update.Properties, out short rssi)) return;

            var address = ExtractAddressFromId(update.Id);
            if (string.IsNullOrEmpty(address)) return;

            UpdateRssiIfChanged(address, rssi);

            // 注意：DeviceWatcher 返回的 RSSI 可能是 Windows 缓存的值
            // 不应该作为"真实 RSSI"使用，只有 Inquiry 成功才能确认设备在范围内
            // 因此这里不更新 _lastRealRssiTime
        }

        private void Watcher_Removed(DeviceWatcher watcher, DeviceInformationUpdate update)
        {
            var address = ExtractAddressFromId(update.Id);
            if (string.IsNullOrEmpty(address)) return;

            if (_devices.TryGetValue(address, out var device))
            {
                if (!device.IsPaired)
                {
                    device.Rssi = -100;
                    device.IsInRange = false;
                }
            }
        }

        private void Watcher_EnumerationCompleted(DeviceWatcher watcher, object _)
        {
            LogHelper.WriteLine($"蓝牙枚举完成，发现 {_devices.Count} 个设备");
        }

        private void Watcher_Stopped(DeviceWatcher watcher, object _)
        {
            LogHelper.WriteLine("DeviceWatcher 已停止");
        }

        #endregion

        #region 定时器回调

        private void RescanCallback(object state)
        {
            if (!_isRunning) return;

            try
            {
                RestartWatcher();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"重扫描失败: {ex.Message}");
            }
        }

        private void CleanupCallback(object state)
        {
            if (!_isRunning) return;

            try
            {
                var now = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(DeviceTimeoutSeconds);

                foreach (var kvp in _devices)
                {
                    var device = kvp.Value;
                    if (!device.IsPaired && (now - device.LastSeen) > timeout)
                    {
                        _devices.TryRemove(kvp.Key, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"清理超时设备失败: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        private static string NormalizeAddress(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var bracketStart = s.LastIndexOf('[');
            var bracketEnd = s.LastIndexOf(']');
            if (bracketStart >= 0 && bracketEnd > bracketStart)
            {
                s = s.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            }

            var matches = MacRegex.Matches(s);
            if (matches.Count > 0)
                return matches[matches.Count - 1].Value.ToUpperInvariant();

            var idx = s.LastIndexOf('-');
            if (idx >= 0 && idx + 1 < s.Length)
                return s.Substring(idx + 1).Trim().ToUpperInvariant();

            // 尝试格式化为标准 MAC 地址
            var hex = Regex.Replace(s, "[^0-9A-Fa-f]", "");
            if (hex.Length == 12)
            {
                return string.Join(":",
                    Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToUpperInvariant();
            }

            return s.Trim().ToUpperInvariant();
        }

        private static string FormatMacAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;

            var hex = Regex.Replace(address, "[^0-9A-Fa-f]", "");
            if (hex.Length != 12) return address.ToUpperInvariant();

            return string.Join(":",
                Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToUpperInvariant();
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

        private static string ExtractAddressFromId(string id)
        {
            return NormalizeAddress(id);
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
            if (props == null) return false;
            if (!props.TryGetValue(IsPairedProperty, out var val) || val == null) return false;

            try
            {
                return Convert.ToBoolean(val);
            }
            catch
            {
                return false;
            }
        }

        private void UpsertDevice(string address, MybluetoothDevice newDevice, short rssi)
        {
            _devices.AddOrUpdate(
                address,
                addValue: newDevice,
                updateValueFactory: (_, existing) =>
                {
                    if (!string.IsNullOrEmpty(newDevice.Name) &&
                        !string.Equals(existing.Name, newDevice.Name, StringComparison.Ordinal))
                        existing.Name = newDevice.Name;

                    if (rssi != 0 && Math.Abs(existing.Rssi - rssi) >= RssiDeltaThreshold)
                        existing.Rssi = rssi;

                    existing.LastSeen = DateTime.Now;
                    // 注意：不要在这里设置 IsInRange！
                    // DeviceWatcher 返回的是缓存数据，不能覆盖连接测试的结果
                    // IsInRange 只能由 UpdateDeviceStatus 设置（基于连接测试结果）
                    if (newDevice.IsPaired)
                        existing.IsPaired = true;

                    return existing;
                });
        }

        private void UpdateRssiIfChanged(string address, short rssi)
        {
            _devices.AddOrUpdate(
                address,
                addValue: new MybluetoothDevice
                {
                    Name = "",
                    Address = address,
                    Rssi = rssi,
                    Type = _bleType == 1 ? "Classic" : "BLE",
                    IsInRange = true,
                    LastSeen = DateTime.Now
                },
                updateValueFactory: (_, existing) =>
                {
                    if (Math.Abs(existing.Rssi - rssi) >= RssiDeltaThreshold)
                        existing.Rssi = rssi;

                    existing.LastSeen = DateTime.Now;
                    existing.IsInRange = true;
                    return existing;
                });
        }

        #endregion
    }
}

