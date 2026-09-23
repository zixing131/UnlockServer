using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
        public bool lockWhenSeen;
        public int bletype;
        public int rssiyuzhi = -70;
        public int hysteresisDb = 8;
        public int presenceTimeout = 8;
        public bool requireAllDevices;
        public bool useLocalUnlock = true;
        public int actionWarnSeconds = 10;
        public bool lockOnlyWhenIdle = true;
        public int idleLockSeconds = 30;

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
        private enum PendingKind { None, Lock, Unlock }
        private PendingKind _pending = PendingKind.None;
        private DateTime _pendingUntil = DateTime.MinValue;
        private bool _userCancelledLock;
        private bool _userCancelledUnlock;
        private bool _seenNearbyAfterManualUnlock;
        private ActionToast _actionToast;
        public Action<bool> UnlockTestFinished;

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
                sessionSwitchClass.SessionLockAction = () =>
                {
                    LocalUnlock.ClearUnlockHint();
                    _bluetooth?.NotifySessionLocked();
                };
                sessionSwitchClass.SessionUnlockAction = () =>
                {
                    _seenNearbyAfterManualUnlock = false;
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
            DismissPending();
            sessionSwitchClass?.Close();
            _bluetooth?.RemoveScanUser();
            LogHelper.WriteLine("解锁监控已停止");
        }

        public void ApplyRuntimeSettings(int threshold, int hysteresis, int timeout, int lockSec, int unlockSec,
            int warnSec, bool autoLock, bool autoUnlock, bool manualLock, bool manualUnlock, bool requireAll, bool localUnlock,
            bool onlyIdle, int idleSec, bool relockWhenSeen)
        {
            rssiyuzhi = threshold;
            hysteresisDb = hysteresis < 0 ? 0 : hysteresis;
            presenceTimeout = timeout;
            lockDelay = lockSec;
            unlockDelay = unlockSec;
            actionWarnSeconds = warnSec < 0 ? 0 : warnSec;
            isautolock = autoLock;
            isautounlock = autoUnlock;
            manuallock = manualLock;
            manualunlock = manualUnlock;
            lockWhenSeen = relockWhenSeen;
            requireAllDevices = requireAll;
            useLocalUnlock = localUnlock;
            lockOnlyWhenIdle = onlyIdle;
            idleLockSeconds = idleSec < 1 ? 1 : idleSec;
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
                int freshInRangeCount = 0;
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
                    bool freshHere = false;

                    if (connected)
                    {
                        if (found != null)
                        {
                            rssi = found.Rssi;
                            real = found.Rssi > -100 && found.LastSeen != DateTime.MinValue;
                        }
                        inRange = true;
                        freshHere = true;
                        status = real && rssi > -100 ? $"{rssi} dBm · 已连接" : "已连接";
                    }
                    else if (found == null)
                    {
                        if (holdPresence && _inRangeByAddress.TryGetValue(bound.Address, out var prevMissing))
                        {
                            inRange = prevMissing;
                            status = islocked ? "锁屏中" : "正在寻找";
                        }
                        else
                        {
                            inRange = false;
                            status = "不在附近";
                        }
                    }
                    else if (found.LastSeen == DateTime.MinValue && found.Rssi <= -100)
                    {
                        if (holdPresence && _inRangeByAddress.TryGetValue(bound.Address, out var prevWait))
                        {
                            inRange = prevWait;
                            status = "正在寻找";
                        }
                        else
                        {
                            inRange = false;
                            status = "正在寻找";
                        }
                    }
                    else
                    {
                        rssi = found.Rssi;
                        real = rssi > -100 && found.LastSeen != DateTime.MinValue;
                        var stale = found.LastSeen == DateTime.MinValue ||
                                    (DateTime.Now - found.LastSeen).TotalSeconds > Math.Max(presenceTimeout, 8);
                        if (stale)
                        {
                            if (holdPresence && _inRangeByAddress.TryGetValue(bound.Address, out var prevStale))
                            {
                                inRange = prevStale;
                                status = islocked ? "锁屏中" : "正在寻找";
                            }
                            else
                            {
                                inRange = false;
                                status = "已离开";
                            }
                        }
                        else
                        {
                            _inRangeByAddress.TryGetValue(bound.Address, out var prev);
                            inRange = ApplyHysteresis(bound.Address, rssi, real, rssi > -100, prev);
                            freshHere = inRange;
                            status = real ? $"{rssi} dBm" : "在附近";
                        }
                    }

                    _inRangeByAddress[bound.Address] = inRange;
                    if (inRange)
                    {
                        inRangeCount++;
                        if (rssi > bestRssi) bestRssi = rssi;
                    }
                    if (freshHere) freshInRangeCount++;

                    UpdateDevicePresence?.Invoke(bound.Address, rssi, inRange, status);
                }

                bool isInRange = requireAllDevices ? inRangeCount == enabled.Count : inRangeCount > 0;
                bool freshInRange = requireAllDevices ? freshInRangeCount == enabled.Count : freshInRangeCount > 0;
                _combinedInRange = isInRange;

                var title = BuildPresenceSummary(enabled, inRangeCount, bestRssi, isInRange);
                UpdategRssi?.Invoke(title, isInRange);

                var shouldLog = (DateTime.Now - _lastTickLog).TotalSeconds >= 15;
                if (shouldLog)
                {
                    _lastTickLog = DateTime.Now;
                    LogHelper.WriteLine(scanHealthy
                        ? $"监控: {inRangeCount}/{enabled.Count} 在范围内"
                        : $"监控: 扫描暂无广播 {scanSilence}s，保持上次状态 {inRangeCount}/{enabled.Count}");
                }

                if (_unlockTestRunning)
                    return;

                bool clickHere = freshInRange || (isInRange && !scanHealthy);

                if (freshInRange)
                    LocalUnlock.ClearUnlockHint();

                if (LocalUnlock.ConsumeUnlockNow())
                {
                    HandleClickUnlock(islocked, clickHere);
                    return;
                }

                if (!isautolock && !isautounlock)
                    return;

                if (LocalUnlock.ConsumeUnlockCancel())
                {
                    if (_pending == PendingKind.Unlock)
                    {
                        UserCancelPending();
                        return;
                    }
                }
                if (_pending != PendingKind.None && DateTime.Now >= _pendingUntil)
                {
                    ExecutePending(_pending);
                    return;
                }

                if (isInRange)
                {
                    if (!islocked && freshInRange && sessionSwitchClass != null && !sessionSwitchClass.isUnlockBySoft)
                        _seenNearbyAfterManualUnlock = true;
                    _deviceLeftTime = null;
                    _userCancelledLock = false;
                    if (_pending == PendingKind.Lock)
                        DismissPending();
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

                        if (_userCancelledUnlock)
                        {
                            if (shouldLog) LogHelper.WriteLine("用户已取消本次解锁");
                            return;
                        }

                        var timeInRange = DateTime.Now - _deviceEnteredTime.Value;
                        if (timeInRange >= UnlockDelayTime &&
                            (DateTime.Now - lastUnLockTime) >= UnlockCooldown)
                        {
                            RequestAction(PendingKind.Unlock);
                        }
                    }
                    else if (_pending == PendingKind.Unlock)
                    {
                        DismissPending();
                    }
                }
                else if (!scanHealthy)
                {
                    if (shouldLog) LogHelper.WriteLine("蓝牙扫描中断，暂不按离开处理");
                }
                else
                {
                    _deviceEnteredTime = null;
                    _userCancelledUnlock = false;
                    if (_pending == PendingKind.Unlock)
                        DismissPending();
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

                        if (!sessionSwitchClass.isUnlockBySoft && manualunlock &&
                            !(lockWhenSeen && _seenNearbyAfterManualUnlock))
                        {
                            if (shouldLog) LogHelper.WriteLine("当前为人工解锁，已设置不干预");
                            return;
                        }

                        if (_userCancelledLock)
                        {
                            if (shouldLog) LogHelper.WriteLine("用户已取消本次锁屏");
                            return;
                        }

                        if (lockOnlyWhenIdle && GetIdleSeconds() < idleLockSeconds)
                        {
                            if (_pending == PendingKind.Lock)
                                DismissPending();
                            if (shouldLog) LogHelper.WriteLine("电脑仍在使用，暂不锁屏");
                            return;
                        }

                        var timeOutOfRange = DateTime.Now - _deviceLeftTime.Value;
                        if (timeOutOfRange >= LockDelayTime &&
                            (DateTime.Now - lastLockTime) >= LockCooldown)
                        {
                            RequestAction(PendingKind.Lock);
                        }
                    }
                    else if (_pending == PendingKind.Lock)
                    {
                        DismissPending();
                    }
                }
            }
        }

        private string BuildPresenceSummary(List<BoundDevice> enabled, int inRangeCount, short bestRssi, bool isInRange)
        {
            if (_bluetooth != null && _bluetooth.LooksLikeScanStall())
                return "正在重新寻找设备";

            if (enabled.Count == 1)
            {
                var name = string.IsNullOrWhiteSpace(enabled[0].DisplayName) ? "设备" : enabled[0].DisplayName;
                if (isInRange)
                    return bestRssi > -100 ? $"{name} 在附近  ·  {bestRssi} dBm" : $"{name} 在附近";
                return $"{name} 不在附近";
            }

            if (isInRange)
                return bestRssi > -100
                    ? $"{inRangeCount}/{enabled.Count} 台在附近  ·  {bestRssi} dBm"
                    : $"{inRangeCount}/{enabled.Count} 台在附近";
            return "设备不在附近";
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

        private void HandleClickUnlock(bool islocked, bool deviceHere)
        {
            if (!islocked)
            {
                LocalUnlock.ClearUnlockHint();
                return;
            }

            if (!deviceHere)
            {
                LogHelper.WriteLine("点击解锁：设备不在旁边");
                LocalUnlock.WriteUnlockHint("away");
                return;
            }

            LogHelper.WriteLine("点击解锁：设备在旁边，按自动解锁处理");
            _userCancelledUnlock = false;
            DismissPending();
            if (sessionSwitchClass != null)
            {
                sessionSwitchClass.dounlocking = true;
                sessionSwitchClass.isLockBySoft = false;
            }
            if (!DoUnlock())
            {
                if (sessionSwitchClass != null)
                    sessionSwitchClass.dounlocking = false;
                LogHelper.WriteLine("点击解锁失败");
                LocalUnlock.WriteUnlockHint("fail");
            }
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

        [StructLayout(LayoutKind.Sequential)]
        private struct LastInputInfo
        {
            public uint Size;
            public uint Time;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LastInputInfo plii);

        private static int GetIdleSeconds()
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf(typeof(LastInputInfo)) };
            if (!GetLastInputInfo(ref info))
                return 0;
            var idle = unchecked(Environment.TickCount - (int)info.Time);
            return idle < 0 ? 0 : idle / 1000;
        }

        private void RequestAction(PendingKind kind)
        {
            if (_pending == kind) return;
            if (_pending != PendingKind.None)
                DismissPending();

            if (actionWarnSeconds <= 0)
            {
                ExecuteAction(kind);
                return;
            }

            _pending = kind;
            _pendingUntil = DateTime.Now.AddSeconds(actionWarnSeconds);
            var title = kind == PendingKind.Lock ? "即将锁屏" : "即将解锁";
            LogHelper.WriteLine($"{title}，{actionWarnSeconds} 秒内可取消");

            bool locked = (sessionSwitchClass != null && sessionSwitchClass.IsLocked)
                || WanClient.IsSessionLocked();
            if (kind == PendingKind.Unlock)
                LocalUnlock.WriteUnlockWarn(actionWarnSeconds);

            if (kind == PendingKind.Unlock && locked)
                return;

            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                lock (lockLock)
                {
                    if (_pending != kind) return;
                    _actionToast?.Dismiss();
                    _actionToast = ToastService.ShowAction(
                        title,
                        "点击这条提示可取消本次操作",
                        actionWarnSeconds,
                        () =>
                        {
                            lock (lockLock) UserCancelPending();
                        },
                        () =>
                        {
                            lock (lockLock) ExecutePending(kind);
                        });
                }
            }));
        }

        private void ExecutePending(PendingKind kind)
        {
            if (_pending != kind) return;
            _pending = PendingKind.None;
            _pendingUntil = DateTime.MinValue;
            _actionToast = null;
            LocalUnlock.ClearUnlockWarn();
            ExecuteAction(kind);
        }

        private void ExecuteAction(PendingKind kind)
        {
            if (kind == PendingKind.Lock)
            {
                LogHelper.WriteLine("执行锁屏");
                if (sessionSwitchClass != null)
                {
                    sessionSwitchClass.dolocking = true;
                    sessionSwitchClass.isLockBySoft = true;
                }
                DoLock();
            }
            else if (kind == PendingKind.Unlock)
            {
                LogHelper.WriteLine("执行解锁");
                if (sessionSwitchClass != null)
                {
                    sessionSwitchClass.dounlocking = true;
                    sessionSwitchClass.isLockBySoft = false;
                }
                if (!DoUnlock()) isunlockfail = true;
            }
        }

        private void UserCancelPending()
        {
            if (_pending == PendingKind.None) return;
            var kind = _pending;
            _pending = PendingKind.None;
            _pendingUntil = DateTime.MinValue;
            _actionToast = null;
            LocalUnlock.ClearUnlockWarn();
            if (kind == PendingKind.Lock)
            {
                _userCancelledLock = true;
                lastLockTime = DateTime.Now;
                LogHelper.WriteLine("用户取消本次锁屏");
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    ToastService.Show("已取消本次锁屏")));
            }
            else
            {
                _userCancelledUnlock = true;
                lastUnLockTime = DateTime.Now;
                LogHelper.WriteLine("用户取消本次解锁");
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    ToastService.Show("已取消本次解锁")));
            }
        }

        private void DismissPending()
        {
            if (_pending == PendingKind.None && _actionToast == null) return;
            _pending = PendingKind.None;
            _pendingUntil = DateTime.MinValue;
            var toast = _actionToast;
            _actionToast = null;
            LocalUnlock.ClearUnlockWarn();
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() => toast?.Dismiss()));
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
                bool ok = false;
                try
                {
                    _bluetooth?.NotifySessionLocked();
                    DoLock();
                    Thread.Sleep(wait * 1000);
                    sessionSwitchClass.dounlocking = true;
                    var prevLocal = useLocalUnlock;
                    useLocalUnlock = LocalUnlock.CanUnlock() || prevLocal;
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
                    var done = UnlockTestFinished;
                    if (done != null)
                    {
                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.CheckAccess())
                            dispatcher.BeginInvoke(new Action(() => done(ok)));
                        else
                            done(ok);
                    }
                }
            });
            return true;
        }
    }
}
