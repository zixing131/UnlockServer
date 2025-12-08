using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using InTheHand.Net.Bluetooth;
using UnlockServer.Views;

namespace UnlockServer
{
    /// <summary>
    /// 解锁管理类
    /// </summary>
    public class UnlockManager
    {
        #region 字段

        private BluetoothDiscover bluetoothDiscover;
        public SessionSwitchClass sessionSwitchClass;

        public bool isautolock = false;
        public bool isautounlock = false;
        public bool manuallock = true;
        public bool manualunlock = false;
        public int bletype = 1;
        public int rssiyuzhi = -90;

        /// <summary>
        /// RSSI 更新回调
        /// 参数：显示文本, 是否为真实RSSI值
        /// </summary>
        public Action<string, bool> UpdategRssi;

        private string unlockaddress = "";
        private string normalizedUnlockAddress = "";
        private bool isrunning = false;

        private int locktimecount = 0;
        private bool isunlockfail = false;
        private readonly object lockLock = new object();

        // 锁屏延迟：不在范围内持续多少秒后锁屏
        private TimeSpan LockDelayTime = TimeSpan.FromSeconds(20);
        // 解锁延迟：在范围内持续多少秒后解锁
        private TimeSpan UnlockDelayTime = TimeSpan.FromSeconds(10);
        // 防止重复操作的冷却时间
        private TimeSpan LockCooldown = TimeSpan.FromSeconds(30);
        
        /// <summary>
        /// 设置锁定延迟（秒）
        /// </summary>
        public int lockDelay
        {
            get => (int)LockDelayTime.TotalSeconds;
            set => LockDelayTime = TimeSpan.FromSeconds(value > 0 ? value : 20);
        }
        
        /// <summary>
        /// 设置解锁延迟（秒）
        /// </summary>
        public int unlockDelay
        {
            get => (int)UnlockDelayTime.TotalSeconds;
            set => UnlockDelayTime = TimeSpan.FromSeconds(value > 0 ? value : 10);
        }
        private TimeSpan UnlockCooldown = TimeSpan.FromSeconds(15);
        
        private DateTime lastLockTime = DateTime.MinValue;
        private DateTime lastUnLockTime = DateTime.MinValue;
        
        // 设备离开/进入范围的时间追踪
        private DateTime? _deviceLeftTime = null;      // 设备离开的时间
        private DateTime? _deviceEnteredTime = null;   // 设备进入范围的时间

        // 缓存最后一次有效的 RSSI
        private short _lastKnownRssi = -100;
        private DateTime _lastRssiUpdate = DateTime.MinValue;
        private readonly TimeSpan _rssiTimeout = TimeSpan.FromSeconds(5); // 缩短到5秒
        
        // 设备状态
        private bool _deviceInRange = false;
        private bool _lastIsRealRssi = false;

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置解锁设备地址
        /// </summary>
        public void setunlockaddress(string address)
        {
            try
            {
                // 尝试从 "Name[AA:BB:CC:DD:EE:FF]" 格式中提取地址
                var match = Regex.Match(address ?? "", @"\[([0-9A-Fa-f:]+)\]");
                if (match.Success)
                {
                    address = match.Groups[1].Value;
                }
            }
            catch { }

            unlockaddress = address ?? "";
            normalizedUnlockAddress = NormalizeAddress(address);

            // 更新 BluetoothDiscover 的目标地址
            bluetoothDiscover?.SetTargetAddress(normalizedUnlockAddress);

            LogHelper.WriteLine($"设置解锁设备地址: {normalizedUnlockAddress}");
        }

        /// <summary>
        /// 验证蓝牙地址格式
        /// </summary>
        public static bool IsValidBluetoothAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
                return false;

            string pattern = @"^([0-9A-Fa-f]{2}[:-]?){5}[0-9A-Fa-f]{2}$";
            return Regex.IsMatch(address, pattern);
        }

        /// <summary>
        /// 启动监控
        /// </summary>
        public void Start()
        {
            sessionSwitchClass = new SessionSwitchClass();

            try
            {
                bluetoothDiscover = new BluetoothDiscover(bletype);

                // 订阅 RSSI 更新事件
                bluetoothDiscover.OnRssiUpdated += OnRssiUpdatedHandler;
                
                // 订阅设备状态变化事件
                bluetoothDiscover.OnDeviceStatusChanged += OnDeviceStatusChangedHandler;

                // 设置目标地址
                if (!string.IsNullOrEmpty(normalizedUnlockAddress))
                {
                    bluetoothDiscover.SetTargetAddress(normalizedUnlockAddress);
                }

                bluetoothDiscover.StartDiscover();

                // 检查蓝牙适配器
                BluetoothRadio radio = BluetoothRadio.Default;
                if (radio == null)
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        MessageDialog.ShowError("没有找到本机蓝牙设备！");
                    });
                    return;
                }

                LogHelper.WriteLine($"蓝牙适配器: {radio.Name}, 模式: {radio.Mode}");

                // 延迟启动监控循环
                Task.Delay(3000).ContinueWith((r) =>
                {
                    isrunning = true;
                    while (isrunning)
                    {
                        try
                        {
                            Tick();
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLine($"监控循环错误: {ex.Message}");
                        }
                        Thread.Sleep(1000);
                    }
                }, TaskContinuationOptions.LongRunning);

                LogHelper.WriteLine("解锁监控已启动");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动蓝牙监控失败: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    MessageDialog.ShowError("启动蓝牙监控失败，可能没有蓝牙硬件或者不兼容！");
                });
            }
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void Stop()
        {
            isrunning = false;
            sessionSwitchClass?.Close();

            if (bluetoothDiscover != null)
            {
                bluetoothDiscover.OnRssiUpdated -= OnRssiUpdatedHandler;
                bluetoothDiscover.OnDeviceStatusChanged -= OnDeviceStatusChangedHandler;
                bluetoothDiscover.StopDiscover();
            }

            LogHelper.WriteLine("解锁监控已停止");
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// RSSI 更新处理
        /// </summary>
        private void OnRssiUpdatedHandler(string address, short rssi, bool isRealRssi)
        {
            if (address.Equals(normalizedUnlockAddress, StringComparison.OrdinalIgnoreCase))
            {
                _lastKnownRssi = rssi;
                _lastRssiUpdate = DateTime.Now;
                _deviceInRange = rssi > -100;
                _lastIsRealRssi = isRealRssi;
                
                // 根据是否为真实值决定显示内容
                if (isRealRssi)
                {
                    // 真实 RSSI 值
                    UpdategRssi?.Invoke($"{rssi} dBm", true);
                }
                else
                {
                    // 模拟值，显示状态
                    var statusText = rssi > -100 ? "✓ 在范围内" : "✗ 不在范围";
                    UpdategRssi?.Invoke(statusText, false);
                }
            }
        }

        /// <summary>
        /// 设备状态变化处理
        /// </summary>
        private void OnDeviceStatusChangedHandler(string address, bool isInRange)
        {
            if (address.Equals(normalizedUnlockAddress, StringComparison.OrdinalIgnoreCase))
            {
                _deviceInRange = isInRange;
                LogHelper.WriteLine($"设备 {address} 状态: {(isInRange ? "在范围内" : "不在范围内")}");
            }
        }

        /// <summary>
        /// 监控循环（每秒执行一次）
        /// </summary>
        private void Tick()
        {
            // 调试：每30秒输出一次 Tick 状态
            if (DateTime.Now.Second % 30 == 0)
            {
                LogHelper.WriteLine($"Tick状态: autolock={isautolock}, autounlock={isautounlock}, address={normalizedUnlockAddress}");
            }
            
            if (!isautolock && !isautounlock)
            {
                // 自动锁屏和自动解锁都未启用
                return;
            }

            if (string.IsNullOrWhiteSpace(normalizedUnlockAddress))
            {
                // 每30秒才输出一次日志，避免刷屏
                if (DateTime.Now.Second % 30 == 0)
                {
                    LogHelper.WriteLine("Tick: 未设置解锁设备地址");
                }
                return;
            }
            
            // 服务器配置检查移到解锁逻辑中，锁屏不需要服务器

            lock (lockLock)
            {
                bool islocked = WanClient.IsSessionLocked();

                if (!islocked)
                {
                    isunlockfail = false;
                }

                if (isunlockfail)
                {
                    locktimecount++;
                    return;
                }

                if (locktimecount >= 120)
                {
                    isunlockfail = false;
                    locktimecount = 0;
                }

                if (bluetoothDiscover == null) return;

                var devices = bluetoothDiscover.getAllDevice();
                var device = devices.FirstOrDefault(p =>
                    p.Address.Equals(normalizedUnlockAddress, StringComparison.OrdinalIgnoreCase));

                // 判断设备是否在范围内
                bool isInRange = false;
                short currentRssi = -100;
                string rangeReason = "";

                if (device != null)
                {
                    currentRssi = device.Rssi;
                    
                    // 优先使用 device.IsInRange（由 BluetoothDiscover 维护的连接测试结果）
                    // 连接测试是最可靠的判断依据
                    if (device.IsInRange)
                    {
                        // 设备在范围内（连接测试成功）
                        // 检查是否有有效的真实 RSSI（在合理范围内：-20 到 -90 之间）
                        bool isValidRssi = currentRssi >= -90 && currentRssi <= -20;
                        
                        if (isValidRssi && currentRssi < rssiyuzhi)
                        {
                            // 有有效真实 RSSI 但信号弱，不在范围
                            isInRange = false;
                            rangeReason = $"真实RSSI {currentRssi}dBm < {rssiyuzhi}dBm";
                        }
                        else
                        {
                            // 连接测试成功就认为在范围内
                            // 无效 RSSI（-128, -100, -40, 0 等）不影响判断
                            isInRange = true;
                            rangeReason = isValidRssi 
                                ? $"真实RSSI {currentRssi}dBm >= {rssiyuzhi}dBm"
                                : "连接测试成功";
                        }
                    }
                    else
                    {
                        // 设备不在范围内（连接测试失败）
                        isInRange = false;
                        rangeReason = "连接测试失败";
                    }
                }
                else
                {
                    // 设备不在列表中
                    isInRange = _deviceInRange;  // 使用上次的状态
                    rangeReason = "设备未在列表中";
                }
                
                // 同步更新 UI 显示（确保 UI 和判断逻辑一致）
                _deviceInRange = isInRange;
                if (isInRange)
                {
                    // 有有效 RSSI 显示 RSSI，否则显示"在范围内"
                    bool isValidRssi = currentRssi >= -90 && currentRssi <= -20;
                    if (isValidRssi)
                    {
                        UpdategRssi?.Invoke($"{currentRssi} dBm", true);
                    }
                    else
                    {
                        UpdategRssi?.Invoke("✓ 在范围内", false);
                    }
                }
                else
                {
                    UpdategRssi?.Invoke("✗ 不在范围", false);
                }
                
                if (isInRange)
                {
                    LogHelper.WriteLine($"设备在范围内: {device?.Name ?? "未知"}[{normalizedUnlockAddress}] ({rangeReason})");
                    
                    // 设备进入范围
                    _deviceLeftTime = null;  // 清除离开时间
                    
                    if (_deviceEnteredTime == null)
                    {
                        _deviceEnteredTime = DateTime.Now;
                        LogHelper.WriteLine("设备刚进入范围，开始计时...");
                    }
                    
                    // 检查是否需要解锁（在范围内持续一段时间）
                    if (islocked && isautounlock)
                    {
                        // 解锁需要服务器配置
                        if (!WanClient.isConfigVal())
                        {
                            if (DateTime.Now.Second % 30 == 0)
                            {
                                LogHelper.WriteLine("解锁需要服务器配置，请在设置中配置服务器信息");
                            }
                            return;
                        }
                        
                        if (manuallock && !sessionSwitchClass.isLockBySoft)
                        {
                            LogHelper.WriteLine("非软件锁定，不干预！");
                            return;
                        }
                        
                        var timeInRange = DateTime.Now - _deviceEnteredTime.Value;
                        if (timeInRange >= UnlockDelayTime)
                        {
                            // 检查冷却时间
                            if ((DateTime.Now - lastUnLockTime) >= UnlockCooldown)
                            {
                                LogHelper.WriteLine($"设备在范围内已 {timeInRange.TotalSeconds:F0} 秒，执行解锁！");
                                sessionSwitchClass.dounlocking = true;
                                sessionSwitchClass.isLockBySoft = false;
                                
                                bool ret = DoUnlock();
                                if (!ret)
                                {
                                    isunlockfail = true;
                                }
                            }
                        }
                        else
                        {
                            LogHelper.WriteLine($"等待解锁: {timeInRange.TotalSeconds:F0}/{UnlockDelayTime.TotalSeconds} 秒");
                        }
                    }
                }
                else
                {
                    // 设备不在范围内
                    LogHelper.WriteLine($"设备不在范围: {rangeReason}");
                    
                    _deviceEnteredTime = null;  // 清除进入时间
                    
                    if (_deviceLeftTime == null)
                    {
                        _deviceLeftTime = DateTime.Now;
                        LogHelper.WriteLine("设备刚离开范围，开始计时...");
                    }
                    
                    // 检查是否需要锁屏（不在范围内持续一段时间）
                    if (!islocked && isautolock)
                    {
                        if (!sessionSwitchClass.isUnlockBySoft && manualunlock)
                        {
                            LogHelper.WriteLine("非软件解锁，不干预人工解锁！");
                            return;
                        }
                        
                        var timeOutOfRange = DateTime.Now - _deviceLeftTime.Value;
                        if (timeOutOfRange >= LockDelayTime)
                        {
                            // 检查冷却时间
                            if ((DateTime.Now - lastLockTime) >= LockCooldown)
                            {
                                LogHelper.WriteLine($"设备离开已 {timeOutOfRange.TotalSeconds:F0} 秒，执行锁屏！");
                                sessionSwitchClass.dolocking = true;
                                sessionSwitchClass.isLockBySoft = true;
                                DoLock();
                            }
                        }
                        else
                        {
                            LogHelper.WriteLine($"等待锁屏: {timeOutOfRange.TotalSeconds:F0}/{LockDelayTime.TotalSeconds} 秒");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 规范化地址
        /// </summary>
        private static string NormalizeAddress(string address)
        {
            if (string.IsNullOrEmpty(address)) return "";

            // 尝试从 "Name[AA:BB:CC:DD:EE:FF]" 格式中提取
            var match = Regex.Match(address, @"\[([0-9A-Fa-f:]+)\]");
            if (match.Success)
            {
                address = match.Groups[1].Value;
            }

            // 移除所有非十六进制字符，然后重新格式化
            var hex = Regex.Replace(address, "[^0-9A-Fa-f]", "");
            if (hex.Length == 12)
            {
                return string.Join(":",
                    Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToUpperInvariant();
            }

            return address.ToUpperInvariant();
        }

        /// <summary>
        /// 执行锁屏
        /// </summary>
        private void DoLock()
        {
            lastLockTime = DateTime.Now;
            WanClient.LockPc();
            LogHelper.WriteLine("已执行锁屏");
        }

        /// <summary>
        /// 执行解锁
        /// </summary>
        private bool DoUnlock()
        {
            lastUnLockTime = DateTime.Now;
            _deviceEnteredTime = null;  // 重置计时
            var result = WanClient.UnlockPc();
            LogHelper.WriteLine($"已执行解锁，结果: {result}");
            return result;
        }

        #endregion
    }
}
