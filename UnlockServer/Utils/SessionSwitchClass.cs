using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnlockServer
{
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

        public SessionSwitchClass()
        {
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }
         
        public void Close()
        { 
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        }
         
        //当前登录的用户变化（登录、注销和解锁屏）
        public void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                //用户登录
                case SessionSwitchReason.SessionLogon:
                    BeginSessionUnlock();
                    break;
                //解锁屏
                case SessionSwitchReason.SessionUnlock:
                    BeginSessionUnlock();
                    break;
                //锁屏
                case SessionSwitchReason.SessionLock:
                    BeginSessionLock();
                    break;
                //注销
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
                //软件锁定
                isUnlockBySoft = true;
            }
            else
            {
                //锁屏后执行 
                isUnlockBySoft = false; 
            }
            dounlocking = false;
        }

        /// <summary>
        /// 锁屏后执行
        /// </summary>
        private void BeginSessionLock()
        {
            if(dolocking)
            {
                //软件锁定
                isLockBySoft = true;
            }
            else
            {
                //锁屏后执行 
                isLockBySoft = false; 
            }
            dolocking = false;
        }
    }

}
