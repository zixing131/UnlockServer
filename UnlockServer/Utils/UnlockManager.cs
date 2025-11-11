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
                            Console.WriteLine("error:" + ex.Message);
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
                Console.WriteLine("未启用");
                return;
            }
            if (string.IsNullOrWhiteSpace(unlockaddress) || WanClient.isConfigVal() == false)
            {
                //配置无效
                Console.WriteLine("配置无效");
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
                    Console.WriteLine("发现设备:" + device.Name + "[" + device.Address + "] " + device.Rssi + "dBm");
                    UpdategRssi?.Invoke(device.Rssi.ToString());
                    if (device.Rssi < rssiyuzhi)
                    {
                        if (islocked == false)//&& lockCount == 0
                        {
                            if (isautolock)
                            {
                                if (sessionSwitchClass.isUnlockBySoft == false && manualunlock == true)
                                {
                                    Console.WriteLine("非软件解锁，不干预！");
                                    return;
                                }
                                Console.WriteLine("信号强度弱，锁屏！");
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
                                    Console.WriteLine("非软件锁定，不干预！");
                                    return;
                                }
                                Console.WriteLine("信号强度够且处于锁屏状态，解锁！");

                                sessionSwitchClass.dounlocking = true;
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
                                Console.WriteLine("信号强度够且但是未处于锁定状态！");
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
                                Console.WriteLine("非软件解锁，不干预人工解锁！");
                                return;
                            }
                            Console.WriteLine("找不到设备，锁屏！");
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
        /// 锁定超时，默认60秒最多锁定一次，防止找不到设备重复锁定导致电脑无法解锁
        /// </summary>
        private TimeSpan LockTimeOut = TimeSpan.FromMilliseconds(60 * 1000);

        /// <summary>
        /// 解锁超时，默认60秒最多解锁一次
        /// </summary>
        private TimeSpan UnLockTimeOut = TimeSpan.FromMilliseconds(30 * 1000);

        DateTime lastLockTime = DateTime.MinValue;
        DateTime lastUnLockTime = DateTime.MinValue;

        /// <summary>
        /// 超时锁定
        /// </summary>
        private void LockByTimeOut()
        {
            DateTime now = DateTime.Now;

            if ((now - lastLockTime) > LockTimeOut)
            {
                //这里判断时间是否超过 LockTimeOut
                lastLockTime = DateTime.Now;
                WanClient.LockPc();
            }
        }

        /// <summary>
        /// 超时锁定
        /// </summary>
        private bool UnLockByTimeOut()
        {
            DateTime now = DateTime.Now;

            if ((now - lastUnLockTime) > UnLockTimeOut)
            {
                //这里判断时间是否超过 UnLockTimeOut
                lastUnLockTime = DateTime.Now;
                return WanClient.UnlockPc();
            }
            return true;
        }
    }
}
