using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using InTheHand.Net.Bluetooth;
using UnlockServer.Models;
using UnlockServer.Services;
using UnlockServer.Views;

namespace UnlockServer
{
    public class UnlockManager
    {
        private UnlockServer.Services.BluetoothService _bluetooth;
        public SessionSwitchClass sessionSwitchClass;

        public bool isautolock;
        public bool isautounlock;
        public bool manuallock = true;
        public bool manualunlock;
        public int bletype;
        public int rssiyuzhi = -70;
        public int hysteresisDb = 8;
        public int presenceTimeout = 8;
        public bool requireAllDevices;
        public bool useLocalUnlock = true;

        public Action<string, bool> UpdategRssi;
        public Action<string, short, bool, string> UpdateDevicePresence;

        private readonly List<BoundDevice> _bound = new List<BoundDevice>();
        private readonly Dictionary<string, bool> _inRangeByAddress = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private bool isrunning;

        private int locktimecount;
        private bool isunlockfail;
        private readonly object lockLock = new object();

        private TimeSpan LockDelayTime = TimeSpan.FromSeconds(15);
        private TimeSpan UnlockDelayTime = TimeSpan.FromSeconds(3);
        private TimeSpan LockCooldown = TimeSpan.FromSeconds(20);
        private TimeSpan UnlockCooldown = TimeSpan.FromSeconds(8);

        public int lockDelay
        {
            get => (int)LockDelayTime.TotalSeconds;
            set => LockDelayTime = TimeSpan.FromSeconds(value > 0 ? value : 15);
        }

        public int unlockDelay
        {
            get => (int)UnlockDelayTime.TotalSeconds;
            set => UnlockDelayTime = TimeSpan.FromSeconds(value > 0 ? value : 3);
        }

        private DateTime lastLockTime = DateTime.MinValue;
        private DateTime lastUnLockTime = DateTime.MinValue;
        private DateTime? _deviceLeftTime;
        private DateTime? _deviceEnteredTime;
        private DateTime _lastTickLog = DateTime.MinValue;
        private bool _combinedInRange;
        private volatile bool _unlockTestRunning;
        private const int ScanOutageGraceSeconds = 40;
        private DateTime _lastSoftwareLockTime = DateTime.MinValue;
        private DateTime _suppressLockUntil = DateTime.MinValue;

        public static bool IsValidBluetoothAddress(string address)
        {
            return !string.IsNullOrEmpty(address) &&
                   System.Text.RegularExpressions.Regex.IsMatch(address, @"^([0-9A-Fa-f]{2}[:-]?){5}[0-9A-Fa-f]{2}$");
        }

        public void SetBoundDevices(IEnumerable<BoundDevice> devices)
        {
            var previous = new Dictionary<string, bool>(_inRangeByAddress, StringComparer.OrdinalIgnoreCase);
            _bound.Clear();
            _inRangeByAddress.Clear();
            if (devices != null)
            {
                foreach (var d in devices)
                {
                    if (d == null || string.IsNullOrWhiteSpace(d.Address)) continue;
                    d.Address = BluetoothDiscover.NormalizeAddress(d.Address) ?? d.Address;
                    _bound.Add(d);
                    if (previous.TryGetValue(d.Address, out var wasInRange))
                        _inRangeByAddress[d.Address] = wasInRange;
                }
            }

            bletype = ResolveScanType();
            if (_bluetooth != null)
            {
                _bluetooth.BluetoothType = bletype;
                _bluetooth.PinAddresses(_bound.Where(x => x.Enabled).Select(x => x.Address));
            }
            LogHelper.WriteLine($"绑定设备 {_bound.Count} 台，扫描模式 {bletype}");
        }

        public void setunlockaddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            SetBoundDevices(new[]
            {
                new BoundDevice { Address = address, Enabled = true, BluetoothType = bletype == 1 ? 1 : 2 }
            });
        }

        public void Start()
        {
            sessionSwitchClass = new SessionSwitchClass();
            try
            {
                bletype = ResolveScanType();
                _bluetooth = UnlockServer.Services.BluetoothService.Shared;
                _bluetooth.BluetoothType = bletype;
                _bluetooth.PinAddresses(_bound.Where(x => x.Enabled).Select(x => x.Address));
                sessionSwitchClass.SessionLockAction = () => _bluetooth?.NotifySessionLocked();
                sessionSwitchClass.SessionUnlockAction = () =>
                {
                    _bluetooth?.NotifySessionUnlocked();
                    HandleUserUnlockAfterSoftwareLock();
                };
                _bluetooth.AddScanUser();

                var radio = BluetoothRadio.Default;
                if (radio == null)
                {
                    Application.Current?.Dispatcher?.Invoke(() => MessageDialog.ShowError("没有找到本机蓝牙设备！"));
                    return;
                }

                Task.Delay(2000).ContinueWith(r =>
                {
                    isrunning = true;
                    while (isrunning)
                    {
                        try { Tick(); }
                        catch (Exception ex) { LogHelper.WriteLine($"监控循环错误: {ex.Message}"); }
                        Thread.Sleep(1000);
                    }
                }, TaskContinuationOptions.LongRunning);

                LogHelper.WriteLine("解锁监控已启动");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动蓝牙监控失败: {ex.Message}");
                Application.Current?.Dispatcher?.Invoke(() =>
                    MessageDialog.ShowError("启动蓝牙监控失败，可能没有蓝牙硬件或者不兼容！"));
            }
        }

        public void Stop()
        {
            isrunning = false;
            sessionSwitchClass?.Close();
            _bluetooth?.RemoveScanUser();
            LogHelper.WriteLine("解锁监控已停止");
        }

        public void ApplyRuntimeSettings(int threshold, int hysteresis, int timeout, int lockSec, int unlockSec,
            bool autoLock, bool autoUnlock, bool manualLock, bool manualUnlock, bool requireAll, bool localUnlock)
        {
            rssiyuzhi = threshold;
            hysteresisDb = hysteresis < 0 ? 0 : hysteresis;
            presenceTimeout = timeout;
            lockDelay = lockSec;
            unlockDelay = unlockSec;
            isautolock = autoLock;
            isautounlock = autoUnlock;
            manuallock = manualLock;
            manualunlock = manualUnlock;
            requireAllDevices = requireAll;
            useLocalUnlock = localUnlock;
        }

        private int ResolveScanType()
        {
            var enabled = _bound.Where(d => d.Enabled).ToList();
            if (enabled.Count == 0) return 0;
            bool hasClassic = enabled.Any(d => d.BluetoothType == 1);
            bool hasBle = enabled.Any(d => d.BluetoothType != 1);
            if (hasClassic && hasBle) return 0;
            return hasClassic ? 1 : 2;
        }

        private void OnRssiUpdatedHandler(string address, short rssi, bool isRealRssi)
        {
            var text = isRealRssi && rssi > -100 ? $"{rssi} dBm" : (rssi > -100 ? "在范围内" : "不在范围");
            UpdateDevicePresence?.Invoke(address, rssi, rssi > -100, text);
        }

        private void OnDeviceStatusChangedHandler(string address, bool isInRange)
        {
            LogHelper.WriteLine($"设备 {address} 观测状态: {(isInRange ? "在范围内" : "不在范围内")}");
        }

        private void Tick()
        {
            var enabled = _bound.Where(d => d.Enabled).ToList();
            if (enabled.Count == 0) return;

            lock (lockLock)
            {
                bool islocked = (sessionSwitchClass != null && sessionSwitchClass.IsLocked)
                    || WanClient.IsSessionLocked();
                if (!islocked) isunlockfail = false;
                if (isunlockfail)
                {
                    locktimecount++;
                    if (locktimecount < 30) return;
                    isunlockfail = false;
                    locktimecount = 0;
                }
                if (_bluetooth == null) return;

                var snapshot = _bluetooth.GetDevices();
                bool scanHealthy = _bluetooth.IsAdvertisementAlive;
                int scanSilence = _bluetooth.ScanSilenceSeconds;
                int inRangeCount = 0;
                short bestRssi = -100;

                foreach (var bound in enabled)
                {
                    var found = snapshot.FirstOrDefault(p =>
                        p.Address.Equals(bound.Address, StringComparison.OrdinalIgnoreCase));

                    bool inRange;
                    short rssi = -100;
                    bool real = false;
                    string status;
                    bool connected = found?.IsConnected == true || _bluetooth.IsAddressConnected(bound.Address);

                    bool holdPresence = islocked
                        || connected
                        || _bluetooth.IsRefreshing
                        || _bluetooth.LooksLikeScanStall();

                    if (connected)
                    {
                        if (found != null)
                        {
                            rssi = found.Rssi;
                            real = found.Rssi > -100 && found.LastSeen != DateTime.MinValue;
                        }
                        inRange = true;
                        status = real && rssi > -100 ? $"{rssi} dBm" : "已连接";
                    }
                    else if (found == null)
                    {
                        if (holdPresence && _inRangeByAddress.TryGetValue(bound.Address, out var prevMissing))
                        {
                            inRange = prevMissing;
                            status = islocked ? "锁屏保持" : "扫描恢复中";
                        }
                        else
                        {
                            inRange = false;
                            status = "未发现";
                        }
                    }
                    else if (found.LastSeen == DateTime.MinValue && found.Rssi <= -100)
                    {
                        if (holdPresence && _inRangeByAddress.TryGetValue(bound.Address, out var prevWait))
                        {
                            inRange = prevWait;
                            status = "扫描恢复中";
                        }
                        else
                        {
                            inRange = false;
                            status = "等待信号";
                        }
                    }
                    else
                    {
                        rssi = found.Rssi;
                        real = rssi > -100 && found.LastSeen != DateTime.MinValue;
                        var freshWindow = Math.Max(presenceTimeout, 30);
                        var stale = found.LastSeen == DateTime.MinValue ||
                                    (DateTime.Now - found.LastSeen).TotalSeconds > freshWindow;
                        if (stale)
                        {
                            if (holdPresence && _inRangeByAddress.TryGetValue(bound.Address, out var prevStale))
                            {
                                inRange = prevStale;
                                status = islocked ? (rssi > -100 ? $"{rssi} dBm · 锁屏保持" : "锁屏保持") : "扫描恢复中";
                            }
                            else
                            {
                                inRange = false;
                                status = "信号超时";
                            }
                        }
                        else
                        {
                            _inRangeByAddress.TryGetValue(bound.Address, out var prev);
                            inRange = ApplyHysteresis(bound.Address, rssi, real, rssi > -100, prev);
                            status = real ? $"{rssi} dBm" : "在附近";
                        }
                    }

                    _inRangeByAddress[bound.Address] = inRange;
                    if (inRange)
                    {
                        inRangeCount++;
                        if (rssi > bestRssi) bestRssi = rssi;
                    }

                    UpdateDevicePresence?.Invoke(bound.Address, rssi, inRange, status);
                }

                bool isInRange = requireAllDevices ? inRangeCount == enabled.Count : inRangeCount > 0;
                _combinedInRange = isInRange;

                if (!scanHealthy)
                    UpdategRssi?.Invoke($"扫描恢复中  ·  {inRangeCount}/{enabled.Count}", isInRange);
                else if (isInRange && bestRssi > -100)
                    UpdategRssi?.Invoke($"{inRangeCount}/{enabled.Count}  ·  {bestRssi} dBm", true);
                else
                    UpdategRssi?.Invoke(isInRange ? $"✓ {inRangeCount}/{enabled.Count} 在范围内" : $"✗ {inRangeCount}/{enabled.Count} 在范围内", false);

                var shouldLog = (DateTime.Now - _lastTickLog).TotalSeconds >= 15;
                if (shouldLog)
                {
                    _lastTickLog = DateTime.Now;
                    LogHelper.WriteLine(scanHealthy
                        ? $"监控: {inRangeCount}/{enabled.Count} 在范围内"
                        : $"监控: 扫描暂无广播 {scanSilence}s，保持上次状态 {inRangeCount}/{enabled.Count}");
                }

                if (!isautolock && !isautounlock)
                    return;

                if (_unlockTestRunning)
                    return;

                if (isInRange)
                {
                    _deviceLeftTime = null;
                    if (_deviceEnteredTime == null)
                    {
                        _deviceEnteredTime = DateTime.Now;
                        LogHelper.WriteLine("设备进入范围，开始解锁计时");
                    }

                    if (islocked && isautounlock)
                    {
                        if (manuallock && !sessionSwitchClass.isLockBySoft)
                        {
                            if (shouldLog) LogHelper.WriteLine("当前为人工锁定，已设置不干预");
                            return;
                        }

                        var timeInRange = DateTime.Now - _deviceEnteredTime.Value;
                        if (timeInRange >= UnlockDelayTime &&
                            (DateTime.Now - lastUnLockTime) >= UnlockCooldown)
                        {
                            LogHelper.WriteLine("执行解锁");
                            sessionSwitchClass.dounlocking = true;
                            sessionSwitchClass.isLockBySoft = false;
                            if (!DoUnlock()) isunlockfail = true;
                        }
                    }
                }
                else if (!scanHealthy)
                {
                    if (shouldLog) LogHelper.WriteLine("蓝牙扫描中断，暂不按离开处理");
                }
                else
                {
                    _deviceEnteredTime = null;
                    if (_deviceLeftTime == null)
                    {
                        _deviceLeftTime = DateTime.Now;
                        LogHelper.WriteLine("设备离开范围，开始锁定计时");
                    }

                    if (!islocked && isautolock)
                    {
                        if (DateTime.Now < _suppressLockUntil)
                        {
                            if (shouldLog) LogHelper.WriteLine("自动锁屏冷却中，暂不锁屏");
                            return;
                        }

                        if (!sessionSwitchClass.isUnlockBySoft && manualunlock)
                        {
                            if (shouldLog) LogHelper.WriteLine("当前为人工解锁，已设置不干预");
                            return;
                        }

                        var timeOutOfRange = DateTime.Now - _deviceLeftTime.Value;
                        if (timeOutOfRange >= LockDelayTime &&
                            (DateTime.Now - lastLockTime) >= LockCooldown)
                        {
                            LogHelper.WriteLine("执行锁屏");
                            sessionSwitchClass.dolocking = true;
                            sessionSwitchClass.isLockBySoft = true;
                            DoLock();
                        }
                    }
                }
            }
        }

        private bool ApplyHysteresis(string address, short rssi, bool hasRealRssi, bool presenceFlag, bool previous)
        {
            if (!hasRealRssi) return presenceFlag;
            var half = Math.Max(0, hysteresisDb) / 2;
            if (!_inRangeByAddress.ContainsKey(address))
                return rssi >= rssiyuzhi;
            if (previous) return rssi >= rssiyuzhi - half;
            return rssi >= rssiyuzhi + half;
        }

        private void HandleUserUnlockAfterSoftwareLock()
        {
            if (sessionSwitchClass == null || sessionSwitchClass.isUnlockBySoft)
                return;
            if (_lastSoftwareLockTime == DateTime.MinValue)
                return;
            if ((DateTime.Now - _lastSoftwareLockTime).TotalSeconds > 20)
                return;

            _suppressLockUntil = DateTime.Now.AddMinutes(3);
            LogHelper.WriteLine("用户立即解锁，暂停自动锁屏 3 分钟");
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                ToastService.Show("已暂停自动锁屏 3 分钟")));
        }

        public void MarkSoftwareLock()
        {
            _lastSoftwareLockTime = DateTime.Now;
            lastLockTime = DateTime.Now;
        }

        private void DoLock()
        {
            MarkSoftwareLock();
            WanClient.LockPc();
            LogHelper.WriteLine("已执行锁屏");
        }

        private bool DoUnlock()
        {
            lastUnLockTime = DateTime.Now;
            _deviceEnteredTime = null;

            if (useLocalUnlock && LocalUnlock.CanUnlock())
            {
                var local = LocalUnlock.RequestUnlock();
                LogHelper.WriteLine($"本机解锁结果: {local}");
                if (local) return true;
            }

            if (WanClient.isConfigVal())
            {
                var remote = WanClient.UnlockPc();
                LogHelper.WriteLine($"远程解锁结果: {remote}");
                return remote;
            }

            LogHelper.WriteLine("无法解锁：请先启用本机解锁，或配置远程解锁服务");
            return false;
        }

        public bool BeginUnlockTest(int delaySeconds, out string error)
        {
            error = null;
            if (_unlockTestRunning)
            {
                error = "已有解锁测试在进行。";
                return false;
            }

            if (!LocalUnlock.CanUnlock() && !WanClient.isConfigVal())
            {
                error = "请先启用本机解锁，或配置远程解锁服务。";
                return false;
            }

            if (sessionSwitchClass == null)
                sessionSwitchClass = new SessionSwitchClass();

            sessionSwitchClass.dolocking = true;
            sessionSwitchClass.isLockBySoft = true;
            _unlockTestRunning = true;

            var wait = delaySeconds > 0 ? delaySeconds : 5;
            Task.Run(() =>
            {
                try
                {
                    _bluetooth?.NotifySessionLocked();
                    DoLock();
                    Thread.Sleep(wait * 1000);
                    sessionSwitchClass.dounlocking = true;
                    var prevLocal = useLocalUnlock;
                    useLocalUnlock = LocalUnlock.CanUnlock() || prevLocal;
                    bool ok;
                    try { ok = DoUnlock(); }
                    finally { useLocalUnlock = prevLocal; }
                    LogHelper.WriteLine($"解锁测试结果: {ok}");
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLine($"解锁测试失败: {ex.Message}");
                }
                finally
                {
                    _unlockTestRunning = false;
                }
            });
            return true;
        }
    }
}
