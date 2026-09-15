using Microsoft.Win32;
using System;

namespace UnlockServer
{
    /// <summary>
    /// 会话切换监听类
    /// </summary>
    public class SessionSwitchClass
    {
        /// <summary>
        /// 解屏后执行的委托
        /// </summary>
        public Action SessionUnlockAction { get; set; }

        /// <summary>
        /// 锁屏后执行的委托
        /// </summary>
        public Action SessionLockAction { get; set; }

        public bool isUnlockBySoft = false;
        public bool isLockBySoft = true;
        public bool dolocking = false;
        public bool dounlocking = false;
        public bool IsLocked { get; private set; }

        private readonly DateTime _startedAt = DateTime.Now;
        private const int BootGraceSeconds = 90;

        public SessionSwitchClass()
        {
            IsLocked = WanClient.IsSessionLocked();
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }

        public void Close()
        {
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        }

        /// <summary>
        /// 当前登录的用户变化（登录、注销和解锁屏）
        /// </summary>
        public void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLogon:
                    BeginSessionUnlock();
                    break;
                case SessionSwitchReason.SessionUnlock:
                    BeginSessionUnlock();
                    break;
                case SessionSwitchReason.SessionLock:
                    BeginSessionLock();
                    break;
                case SessionSwitchReason.SessionLogoff:
                    break;
            }
        }

        /// <summary>
        /// 解屏、登录后执行
        /// </summary>
        private void BeginSessionUnlock()
        {
            if (dounlocking)
            {
                isUnlockBySoft = true;
            }
            else
            {
                isUnlockBySoft = false;
            }
            dounlocking = false;
            IsLocked = false;

            SessionUnlockAction?.Invoke();
        }

        /// <summary>
        /// 锁屏后执行
        /// </summary>
        private void BeginSessionLock()
        {
            if (dolocking)
            {
                isLockBySoft = true;
            }
            else if ((DateTime.Now - _startedAt).TotalSeconds < BootGraceSeconds)
            {
                // 开机/刚启动时系统锁屏视为允许自动解锁，避免「不干预人工锁定」挡住第一次解锁
                isLockBySoft = true;
            }
            else
            {
                isLockBySoft = false;
            }
            dolocking = false;
            IsLocked = true;

            SessionLockAction?.Invoke();
        }
    }
}

