using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Timers;
using UnlockServer.Models;
using Windows.Devices.Enumeration;
using Timer = System.Timers.Timer;

namespace UnlockServer.Services
{
    /// <summary>
    /// 蓝牙服务实现 - 增强版设备发现（支持已配对设备）
    /// </summary>
    public class BluetoothService : IBluetoothService, IDisposable
    {
        #region 常量和字段

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
        private const string BluetoothClassicId = "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\")";
        private const string BluetoothLEId = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

        private const int RssiDeltaThreshold = 2;
        private const int DeviceTimeoutSeconds = 30;
        private const int RescanIntervalMs = 15000;

        private DeviceWatcher _watcher;
        private Timer _cleanupTimer;
        private Timer _rescanTimer;
        private readonly ConcurrentDictionary<string, BluetoothDeviceModel> _devices;
        private readonly object _lockObj = new object();
        private int _bluetoothType = 1;
        private bool _isDisposed;

        #endregion

        #region 属性

        public bool IsScanning { get; private set; }

        public int BluetoothType
        {
            get => _bluetoothType;
            set
            {
                if (_bluetoothType != value)
                {
                    _bluetoothType = value;
                    if (IsScanning)
                    {
                        StopScan();
                        StartScan();
                    }
                }
            }
        }

        #endregion

        #region 事件

        public event EventHandler<BluetoothDeviceModel> DeviceDiscovered;
        public event EventHandler<BluetoothDeviceModel> DeviceUpdated;
        public event EventHandler<string> DeviceLost;

        #endregion

        #region 构造函数

        public BluetoothService()
        {
            _devices = new ConcurrentDictionary<string, BluetoothDeviceModel>(StringComparer.OrdinalIgnoreCase);
            
            // 设置清理定时器
            _cleanupTimer = new Timer(5000);
            _cleanupTimer.Elapsed += CleanupTimer_Elapsed;

            // 设置重扫描定时器
            _rescanTimer = new Timer(RescanIntervalMs);
            _rescanTimer.Elapsed += RescanTimer_Elapsed;
        }

        #endregion

        #region 公共方法

        public void StartScan()
        {
            if (IsScanning) return;

            try
            {
                StopWatcher();
                _devices.Clear();

                // 先加载已配对设备
                LoadPairedDevices();

                // 启动 DeviceWatcher
                StartWatcher();

                _cleanupTimer.Start();
                _rescanTimer.Start();
                IsScanning = true;

                LogHelper.WriteLine($"蓝牙扫描已启动，类型: {(_bluetoothType == 2 ? "BLE" : "经典蓝牙")}");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动蓝牙扫描失败: {ex.Message}");
                IsScanning = false;
            }
        }

        public void StopScan()
        {
            StopWatcher();
            _cleanupTimer?.Stop();
            _rescanTimer?.Stop();
            IsScanning = false;
            LogHelper.WriteLine("蓝牙扫描已停止");
        }

        public List<BluetoothDeviceModel> GetDevices()
        {
            // 按配对状态和信号强度排序
            return _devices.Values
                .OrderByDescending(d => d.IsPaired)
                .ThenByDescending(d => d.Rssi)
                .ToList();
        }

        public BluetoothDeviceModel GetDeviceByAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;

            var normalizedAddress = NormalizeAddress(address);
            if (string.IsNullOrEmpty(normalizedAddress)) return null;

            _devices.TryGetValue(normalizedAddress, out var device);
            return device;
        }

        #endregion

        #region 已配对设备

        private void LoadPairedDevices()
        {
            // 已配对设备将通过 DeviceWatcher 自动发现
            // IsPaired 属性会从 System.Devices.Aep.IsPaired 获取
            LogHelper.WriteLine("等待 DeviceWatcher 发现已配对设备...");
        }

        #endregion

        #region DeviceWatcher

        private void StartWatcher()
        {
            try
            {
                string selector = _bluetoothType == 2 ? BluetoothLEId : BluetoothClassicId;
                _watcher = DeviceInformation.CreateWatcher(
                    selector, 
                    RequestedProperties, 
                    DeviceInformationKind.AssociationEndpoint);

                _watcher.Added += Watcher_Added;
                _watcher.Updated += Watcher_Updated;
                _watcher.Removed += Watcher_Removed;
                _watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
                _watcher.Stopped += Watcher_Stopped;

                _watcher.Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动 DeviceWatcher 失败: {ex.Message}");
            }
        }

        private void RestartWatcher()
        {
            try
            {
                StopWatcher();
                Thread.Sleep(500);
                StartWatcher();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"重启 DeviceWatcher 失败: {ex.Message}");
            }
        }

        private void Watcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            try
            {
                var address = GetAddressFromDeviceInfo(deviceInfo);
                if (string.IsNullOrEmpty(address)) return;

                if (!TryGetRssi(deviceInfo.Properties, out short rssi))
                {
                    rssi = -100;
                }

                var isPaired = GetIsPaired(deviceInfo.Properties);

                var device = new BluetoothDeviceModel
                {
                    Name = deviceInfo.Name ?? "",
                    Address = address,
                    Rssi = rssi,
                    Type = _bluetoothType == 2 ? "BLE" : "Classic",
                    IsPaired = isPaired,
                    LastSeen = DateTime.Now
                };

                var isNew = !_devices.ContainsKey(address);
                _devices.AddOrUpdate(address, device, (key, existing) =>
                {
                    if (!string.IsNullOrEmpty(device.Name))
                        existing.Name = device.Name;
                    if (rssi != -100 && Math.Abs(existing.Rssi - rssi) >= RssiDeltaThreshold)
                        existing.Rssi = rssi;
                    if (isPaired)
                        existing.IsPaired = true;
                    existing.LastSeen = DateTime.Now;
                    return existing;
                });

                if (isNew)
                {
                    DeviceDiscovered?.Invoke(this, device);
                    LogHelper.WriteLine($"发现设备: {device.DisplayName} [{address}] {rssi}dBm {(isPaired ? "(已配对)" : "")}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"处理设备添加事件失败: {ex.Message}");
            }
        }

        private void Watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            try
            {
                var address = NormalizeAddress(update.Id);
                if (string.IsNullOrEmpty(address)) return;

                if (!TryGetRssi(update.Properties, out short rssi)) return;

                _devices.AddOrUpdate(address,
                    new BluetoothDeviceModel
                    {
                        Name = "",
                        Address = address,
                        Rssi = rssi,
                        Type = _bluetoothType == 2 ? "BLE" : "Classic",
                        LastSeen = DateTime.Now
                    },
                    (key, existing) =>
                    {
                        if (Math.Abs(existing.Rssi - rssi) >= RssiDeltaThreshold)
                        {
                            existing.Rssi = rssi;
                            DeviceUpdated?.Invoke(this, existing);
                        }
                        existing.LastSeen = DateTime.Now;
                        return existing;
                    });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"处理设备更新事件失败: {ex.Message}");
            }
        }

        private void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate update)
        {
            try
            {
                var address = NormalizeAddress(update.Id);
                if (string.IsNullOrEmpty(address)) return;

                // 已配对设备不移除，只标记 RSSI 为默认值
                if (_devices.TryGetValue(address, out var device))
                {
                    if (device.IsPaired)
                    {
                        device.Rssi = -100;
                    }
                    else
                    {
                        if (_devices.TryRemove(address, out _))
                        {
                            DeviceLost?.Invoke(this, address);
                            LogHelper.WriteLine($"设备移除: {device.DisplayName} [{address}]");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"处理设备移除事件失败: {ex.Message}");
            }
        }

        private void Watcher_EnumerationCompleted(DeviceWatcher sender, object args)
        {
            LogHelper.WriteLine($"蓝牙枚举完成，发现 {_devices.Count} 个设备");
        }

        private void Watcher_Stopped(DeviceWatcher sender, object args)
        {
            LogHelper.WriteLine("DeviceWatcher 已停止");
        }

        #endregion

        #region 定时器

        private void CleanupTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var now = DateTime.Now;
            var timeoutDevices = _devices
                .Where(kv => !kv.Value.IsPaired && (now - kv.Value.LastSeen).TotalSeconds > DeviceTimeoutSeconds)
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

        private void RescanTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!IsScanning) return;

            try
            {
                RestartWatcher();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"重扫描失败: {ex.Message}");
            }
        }

        #endregion

        #region 辅助方法

        private void StopWatcher()
        {
            if (_watcher != null)
            {
                try
                {
                    _watcher.Added -= Watcher_Added;
                    _watcher.Updated -= Watcher_Updated;
                    _watcher.Removed -= Watcher_Removed;
                    _watcher.EnumerationCompleted -= Watcher_EnumerationCompleted;
                    _watcher.Stopped -= Watcher_Stopped;

                    if (_watcher.Status == DeviceWatcherStatus.Started ||
                        _watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                    {
                        _watcher.Stop();
                    }
                }
                catch { }
                _watcher = null;
            }
        }

        private static string NormalizeAddress(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var matches = MacRegex.Matches(s);
            if (matches.Count > 0)
                return matches[matches.Count - 1].Value.ToUpperInvariant();

            var idx = s.LastIndexOf('-');
            if (idx >= 0 && idx + 1 < s.Length)
                return s.Substring(idx + 1).Trim().ToUpperInvariant();

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

        private static string GetAddressFromDeviceInfo(DeviceInformation deviceInfo)
        {
            string raw = null;
            if (deviceInfo.Properties != null && 
                deviceInfo.Properties.TryGetValue(DeviceAddressProperty, out var addrObj))
            {
                raw = addrObj?.ToString();
            }

            var norm = NormalizeAddress(raw);
            if (!string.IsNullOrEmpty(norm)) return norm;

            return NormalizeAddress(deviceInfo.Id);
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

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            StopScan();
            _cleanupTimer?.Dispose();
            _rescanTimer?.Dispose();
            _devices.Clear();
        }

        #endregion
    }
}
