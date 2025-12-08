using InTheHand.Net.Bluetooth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UnlockServer
{
    /// <summary>
    /// 解锁管理类
    /// </summary>
    public class UnlockManager
    {


        BluetoothDiscover bluetoothDiscover;
        public bool isautolock = false;
        public bool isautounlock = false;
        /// <summary>
        /// true为不干预人工锁定
        /// </summary>
        public bool manuallock = true;
        /// <summary>
        /// true为不干预人工解锁
        /// </summary>
        public bool manualunlock = false;

        public SessionSwitchClass sessionSwitchClass; 

        public int bletype = 1;

        public  Action<string> UpdategRssi;

        private string unlockaddress = "";

        public void setunlockaddress(string address)
        {
            try
            {
                address = address.Split('[')[1].Split(']')[0].ToLower();
            } catch (Exception ex) { 
            }
            unlockaddress = address;
        }
        public int rssiyuzhi = -90;

        /// <summary>
        /// 验证字符串是否为有效的蓝牙地址格式（更严格的版本）
        /// 允许空格作为分隔符，也允许连字符作为分隔符
        /// </summary>
        /// <param name="address">要验证的蓝牙地址字符串</param>
        /// <returns>如果是有效的蓝牙地址格式返回true，否则返回false</returns>
        public static bool IsValidBluetoothAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
                return false;

            // 正则表达式模式：允许冒号、空格或连字符作为分隔符
            string pattern = @"^([0-9A-Fa-f]{2}[:-]?){5}[0-9A-Fa-f]{2}$";

            return Regex.IsMatch(address, pattern);
        } 

        public bool isrunning = false;
        public void Start()
        {

            sessionSwitchClass = new SessionSwitchClass();
            try
            { 
                bluetoothDiscover = new BluetoothDiscover(bletype);
                bluetoothDiscover.StartDiscover();

                BluetoothRadio radio = BluetoothRadio.Default;//获取蓝牙适配器
                if (radio == null)
                {
                    MessageBox.Show("没有找到本机蓝牙设备！");
                    return;
                }  

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
                            LogHelper.WriteLine("error:" + ex.Message);
                        }
                        Thread.Sleep(1000);
                    }

                }, TaskContinuationOptions.LongRunning);

            }
            catch (Exception ex)
            {
                MessageBox.Show("启动蓝牙监控失败，可能没有蓝牙硬件或者不兼容！");
            }
        }

        public void Stop()
        {
            isrunning = false;
            sessionSwitchClass.Close();
            bluetoothDiscover?.StopDiscover();

        }


        private void Tick()
        {
            if (isautolock == false && isautounlock == false)
            {
                //没有启用
                LogHelper.WriteLine("未启用");
                return;
            }
            if (string.IsNullOrWhiteSpace(unlockaddress) || WanClient.isConfigVal() == false)
            {
                //配置无效
                LogHelper.WriteLine("配置无效");
                return;
            }

            lock (lockLock)
            {

                bool islocked = WanClient.IsSessionLocked();

                if (islocked)
                {
                    //现在是锁定状态 
                }
                else
                {
                    //已经解锁 
                    isunlockfail = false;
                }

                if (isunlockfail)
                {
                    //上次解锁失败
                    locktimecount++;
                    return;
                }
                if (locktimecount >= 120)
                {
                    //重置时间
                    isunlockfail = false;
                    locktimecount = 0;
                }

                if (bluetoothDiscover == null)
                {
                    return;
                }
                var Devices = bluetoothDiscover.getAllDevice();

                MybluetoothDevice device = Devices.FirstOrDefault(p => p.Address.ToLower() == unlockaddress);
                if (device != null)
                {
                    LogHelper.WriteLine("发现设备:" + device.Name + "[" + device.Address + "] " + device.Rssi + "dBm");
                    UpdategRssi?.Invoke(device.Rssi.ToString());
                    if (device.Rssi < rssiyuzhi)
                    {
                        if (islocked == false)//&& lockCount == 0
                        {
                            if (isautolock)
                            {
                                if (sessionSwitchClass.isUnlockBySoft == false && manualunlock == true)
                                {
                                    LogHelper.WriteLine("非软件解锁，不干预！");
                                    return;
                                }
                                LogHelper.WriteLine("信号强度弱，锁屏！");
                                sessionSwitchClass.dolocking = true;
                                //WanClient.LockPc(); 
                                LockByTimeOut();
                            }
                            //lockCount++;
                            //unlockount = 0;
                        }
                    }
                    else
                    {
                        if (islocked)
                        {
                            if (isautounlock)
                            {
                                if (manuallock == true && sessionSwitchClass.isLockBySoft == false)
                                {
                                    //不干预人工解锁
                                    LogHelper.WriteLine("非软件锁定，不干预！");
                                    return;
                                }
                                LogHelper.WriteLine("信号强度够且处于锁屏状态，解锁！");

                                sessionSwitchClass.dounlocking = true;
                                sessionSwitchClass.isLockBySoft = false;
                                bool ret = UnLockByTimeOut();

                                if (ret == false)
                                {
                                    isunlockfail = true;
                                }
                            }
                        }
                        else
                        {
                            if (isautounlock)
                            {
                                LogHelper.WriteLine("信号强度够且但是未处于锁定状态！");
                            }
                        }
                    }
                }
                else
                {
                    if (islocked == false) //  && lockCount == 0
                    {
                        if (isautolock)
                        {
                            if (sessionSwitchClass.isUnlockBySoft == false && manualunlock == true)
                            {
                                LogHelper.WriteLine("非软件解锁，不干预人工解锁！");
                                return;
                            }
                            LogHelper.WriteLine("找不到设备，锁屏！");
                            sessionSwitchClass.dolocking = true;
                            LockByTimeOut();
                        }
                        //lockCount++;
                        //unlockount = 0;
                    }
                }

            }
        }


        private int locktimecount = 0;
        private bool isunlockfail = false;

        private object lockLock = new object();

        /// <summary>
        /// 锁定超时，默认10秒最多锁定一次，防止找不到设备重复锁定导致电脑无法解锁
        /// </summary>
        private TimeSpan LockTimeOut = TimeSpan.FromMilliseconds(10 * 1000);

        /// <summary>
        /// 解锁超时，默认10秒最多解锁一次
        /// </summary>
        private TimeSpan UnLockTimeOut = TimeSpan.FromMilliseconds(10 * 1000);

        DateTime lastLockTime = DateTime.MinValue;
        DateTime lastUnLockTime = DateTime.MinValue;

        /// <summary>
        /// 锁定请求时间队列，用于防波动
        /// </summary>
        private Queue<DateTime> lockRequestQueue = new Queue<DateTime>();
        
        /// <summary>
        /// 锁定防波动时间窗口（1分钟）
        /// </summary>
        private TimeSpan lockWindowTime = TimeSpan.FromMinutes(1);
        
        /// <summary>
        /// 时间窗口内需要达到的锁定请求次数
        /// </summary>
        private const int requiredLockCount = 10;

        /// <summary>
        /// 超时锁定（防波动版本：1分钟内需要10次请求才执行锁定）
        /// </summary>
        private void LockByTimeOut()
        {
            DateTime now = DateTime.Now;

            // 添加当前请求时间到队列
            lockRequestQueue.Enqueue(now);

            // 移除时间窗口之外的旧请求
            while (lockRequestQueue.Count > 0 && (now - lockRequestQueue.Peek()) > lockWindowTime)
            {
                lockRequestQueue.Dequeue();
            }

            // 检查时间窗口内的请求次数
            if (lockRequestQueue.Count >= requiredLockCount)
            {
                // 达到阈值，执行锁定
                if ((now - lastLockTime) > LockTimeOut)
                {
                    LogHelper.WriteLine($"1分钟内锁定请求达到{lockRequestQueue.Count}次，执行锁定");
                    lastLockTime = DateTime.Now;
                    WanClient.LockPc();
                    
                    // 清空队列，避免重复锁定
                    lockRequestQueue.Clear();
                }
            }
            else
            {
                LogHelper.WriteLine($"锁定请求计数: {lockRequestQueue.Count}/{requiredLockCount}（需在1分钟内达到{requiredLockCount}次）");
            }
        }

        /// <summary>
        /// 超时解锁
        /// </summary>
        private bool UnLockByTimeOut()
        {
            DateTime now = DateTime.Now;

            if ((now - lastUnLockTime) > UnLockTimeOut)
            {
                //这里判断时间是否超过 UnLockTimeOut
                lastUnLockTime = DateTime.Now;
                
                // 检测到设备，清空锁定请求队列
                lockRequestQueue.Clear();
                
                return WanClient.UnlockPc();
            }
            return true;
        }
    }
}
