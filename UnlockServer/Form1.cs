using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets; 
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms; 
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace UnlockServer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load; 
            var softwareVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            this.Text = "蓝牙解锁工具_v" + softwareVersion;
            this.notifyIcon1.Visible = true;
            this.FormClosing += Form1_FormClosing;
            this.FormClosed += Form1_FormClosed;

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.ApplicationExitCall)
            {  
                    this.notifyIcon1.Visible = false;
                    Application.Exit(); 
            }
            else
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide(); 
            }
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            try
            { 
                if(unlockManager != null){ 
                    unlockManager.Stop();
                }
            }
            catch (Exception ex)
            {

            }
        } 

        private static UnlockManager unlockManager;
        private void Form1_Load(object sender, EventArgs e)
        {
            unlockManager = new UnlockManager();
            loadConfig();
            unlockManager.Start();
            unlockManager.UpdategRssi = UpdategRssi;
            if (Program.ishideRun)
            { 
                this.Close();
            }
        }
        private void UpdategRssi(string rssi)
        {
            txt_bleDevice.Invoke(new Action(()=>{ 
                txt_bleDevice.Text = txt_bleDevice.Text.Split('-')[0] .Trim()+ " "+rssi + "dBm";
            }));
        }

        int rssiyuzhi = -90;

        private void loadConfig()
        {

            checkBox1.Checked = AutoStartHelper.IsExists();

            txtip.Text = OperateIniFile.ReadSafeString("setting", "ip", txtip.Text);
            txtpt.Text = OperateIniFile.ReadSafeString("setting", "pt", txtpt.Text);
            txtus.Text = OperateIniFile.ReadSafeString("setting", "us", txtus.Text); 
            txtpd.Text = OperateIniFile.ReadSafeString("setting", "pd", txtpd.Text);
            txtrssi.Text = OperateIniFile.ReadSafeString("setting", "rssi", txtrssi.Text);
            int.TryParse(txtrssi.Text, out rssiyuzhi);
            unlockManager.rssiyuzhi = rssiyuzhi;
            var unlockaddress = OperateIniFile.ReadSafeString("setting", "address", "00:00:00:00:00:00"); 
            unlockManager.setunlockaddress(unlockaddress); 
            txt_bleDevice.Text = unlockaddress;
            getBluetoothType();
            reloadLockConfig(); 
            WanClient.reloadConfig();
        }

        private int bletype = 1;
        private void getBluetoothType()
        {
            var type= OperateIniFile.ReadIniInt("setting", "type", 1);
            bletype = type;
            unlockManager.bletype = bletype;
            if (type == 2)
            {
                rdbble.Checked = true;
            }
            else
            {
                rdbclassic.Checked = true;
            }

        }

        private void setBluetoothType()
        {
            int type = 0;
            if (rdbclassic.Checked)
            {
                type = 1;
            }
            if(rdbble.Checked)
            {
                type = 2;
            }
            bletype = type;
            unlockManager.bletype = bletype;
            OperateIniFile.WriteIniInt("setting", "type", type);
        }
         
        private void btn_save_Click(object sender, EventArgs e)
        { 
            try
            {
                if (string.IsNullOrEmpty(txtip.Text))
                {
                    txtip.Focus();
                    MessageBox.Show(this, "IP地址不能为空！");
                    return;
                }
                if (string.IsNullOrEmpty(txtus.Text))
                {
                    txtus.Focus();
                    MessageBox.Show(this, "账号不能为空！");
                    return;
                }

                if (string.IsNullOrEmpty(txtpd.Text))
                {
                    txtpd.Focus();
                    MessageBox.Show(this, "密码不能为空！");
                    return;
                } 

                var address = txt_bleDevice.Text.Trim();
                //if (!UnlockManager.IsValidBluetoothAddress(address))
                //{

                //    MessageBox.Show(this, "蓝牙地址无效！");
                //    return;
                //}
                OperateIniFile.WriteSafeString("setting", "address", address); 
                unlockManager.setunlockaddress(address);
                if (int.TryParse(txtrssi.Text, out rssiyuzhi) == false || rssiyuzhi > 0 || rssiyuzhi < -128)
                {
                    txtrssi.Focus();
                    MessageBox.Show(this, "请输入有效的信号阈值(-128到0)!");
                    return;
                }
                unlockManager.rssiyuzhi = rssiyuzhi; 
                if (int.TryParse(txtpt.Text, out int pt) == false || pt >= 65565 || pt <= 0)
                {
                    txtrssi.Focus();
                    MessageBox.Show(this, "请输入有效的端口号(1到65564)!");
                    return;
                }

                OperateIniFile.WriteSafeString("setting", "ip", txtip.Text);

                OperateIniFile.WriteSafeString("setting", "pt", txtpt.Text);

                OperateIniFile.WriteSafeString("setting", "us", txtus.Text);

                OperateIniFile.WriteSafeString("setting", "pd", txtpd.Text);

                OperateIniFile.WriteSafeString("setting", "rssi", txtrssi.Text);
                setBluetoothType();
                WanClient.reloadConfig();

                MessageBox.Show(this, "保存成功！");
            }catch(Exception ex)
            { 
                MessageBox.Show(this, "保存配置发生错误！");
            }
        }

        Dictionary<string, short> catchedDeviceRssi = new Dictionary<string, short>();
        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            short rssi = eventArgs.RawSignalStrengthInDBm;
            var address = new BluetoothAddress(eventArgs.BluetoothAddress).ToString();
            LogHelper.WriteLine(address + "-----" + rssi);
            if (rssi <= -120)
            {
                if(catchedDeviceRssi.ContainsKey(address))
                {
                    catchedDeviceRssi.Remove(address);
                }
            }
            else
            { 
                catchedDeviceRssi[address] = rssi;
            }
        } 

        private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            if (string.IsNullOrEmpty(args.Name)) { return; }

            var rssi = args.Properties.Single(d => d.Key == "System.Devices.Aep.SignalStrength").Value;
            if (rssi == null) { return; }
            int x1 = int.Parse(rssi.ToString());

            if (x1 < -80) { return; }

            var id = args.Id;
            string s1 = $"[{x1}] {(args.Name).PadLeft(30, ' ')} {args.Id}";
             
        } 
         
        private void button1_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.52pojie.cn/thread-1678522-1-1.html"));  
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Left)
            {
                this.Show();
                this.ShowInTaskbar = true;
                this.WindowState = FormWindowState.Normal;
                this.TopMost = true;
                this.TopMost = false; 
            }
        }

        private void 显示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Show();
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.TopMost = true;
            this.TopMost = false; 
        }

        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("是否退出？", "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            else
            {
                this.notifyIcon1.Visible = false; 
                Application.Exit();
            }
        } 

        private void rdbclassic_CheckedChanged(object sender, EventArgs e)
        {
            setBluetoothType();
        }

        private void rdbble_CheckedChanged(object sender, EventArgs e)
        {
            setBluetoothType(); 
        }
        private void handlerPairingReq(DeviceInformationCustomPairing CP, DevicePairingRequestedEventArgs DPR)
        {
            //so we get here for custom pairing request.
            //this is the magic place where your pin goes.
            //my device actually does not require a pin but
            //windows requires at least a "0".  So this solved 
            //it.  This does not pull up the Windows UI either.
            DPR.Accept("0"); 

        }
        private void button2_Click(object sender, EventArgs e)
        {
            try 
            {  
                string address= txt_bleDevice.Text.Trim();
                //if(!UnlockManager.IsValidBluetoothAddress(address))
                //{
                //    MessageBox.Show(this, "蓝牙地址无效！");
                //    return;
                //} 

                BluetoothAddress ad = BluetoothAddress.Parse(address).ToUInt64();
                BluetoothLEDevice bluetoothLEDevice = BluetoothLEDevice.FromBluetoothAddressAsync(ad).GetAwaiter().GetResult();
                bluetoothLEDevice.DeviceInformation.Pairing.Custom.PairingRequested += handlerPairingReq;

                //var prslt = bluetoothLEDevice.DeviceInformation.Pairing.Custom.PairAsync(DevicePairingKinds.ConfirmOnly, DevicePairingProtectionLevel.None).GetAwaiter().GetResult();

                var prslt = bluetoothLEDevice.DeviceInformation.Pairing.UnpairAsync().GetAwaiter().GetResult();

                BluetoothLEDevice bleDevice = bluetoothLEDevice;
                var gatt = bleDevice.GetGattServicesAsync(BluetoothCacheMode.Cached).GetAwaiter().GetResult();
                var servv = gatt.Services[1];

                MessageBox.Show(this, prslt.Status.ToString()); 
            }catch(Exception ex)
            {
                MessageBox.Show(this, "取消配对发生错误："+ex.Message);
            }
        }
         
        private void checkBox1_Click(object sender, EventArgs e)
        {
            var ischeck = checkBox1.Checked;

            if(ischeck)
            {
                if(AutoStartHelper.IsExists()==false)
                {
                    var ret = AutoStartHelper.AddStart();
                    if(ret==false)
                    { 
                        MessageBox.Show(this, "添加自启失败！");
                    }
                }
            }
            else
            {
                var ret =  AutoStartHelper.RemoveStart();
                if (ret == false)
                {
                    MessageBox.Show(this, "移除自启失败！");
                }
            } 
            checkBox1.Checked = AutoStartHelper.IsExists();
        }
         
        private void ckb_autolock_Click(object sender, EventArgs e)
        {
             
            var ischeck = ckb_autolock.Checked;
            OperateIniFile.WriteIniInt("setting", "autolock", ischeck ? 1 : 0);
            reloadLockConfig();
        }
        /// <summary>
        /// 重载锁定配置
        /// </summary>
        private void reloadLockConfig()
        {
            try
            {
                isautolock = OperateIniFile.ReadIniInt("setting", "autolock", 1) == 1;
                isautounlock = OperateIniFile.ReadIniInt("setting", "autounlock", 1) == 1;
                manuallock = OperateIniFile.ReadIniInt("setting", "manuallock", 1) == 1;

                manualunlock = OperateIniFile.ReadIniInt("setting", "manualunlock", 0) == 1;
                ckb_autolock.Checked = isautolock;
                ckb_autounlock.Checked = isautounlock;
                ckb_manuallock.Checked = manuallock;
                ckb_manuclunlock.Checked = manualunlock;
                unlockManager.isautolock = isautolock;
                unlockManager.isautounlock = isautounlock;
                unlockManager.manuallock = manuallock;
                unlockManager.manualunlock = manualunlock;

            }
            catch (Exception ex)
            {
                LogHelper.WriteLine(ex.Message);
            }
        }

        private bool isautolock = false;
        private bool isautounlock = false;
        /// <summary>
        /// true为不干预人工锁定
        /// </summary>
        private bool manuallock = true;
        /// <summary>
        /// true为不干预人工解锁
        /// </summary>
        private bool manualunlock = false;

        private void ckb_autounlock_Click(object sender, EventArgs e)
        {
            var ischeck = ckb_autounlock.Checked;
            OperateIniFile.WriteIniInt("setting", "autounlock", ischeck ? 1 : 0);
            reloadLockConfig();
        }

        private void ckb_manual_Click(object sender, EventArgs e)
        {
            var ischeck = ckb_manuallock.Checked;
            OperateIniFile.WriteIniInt("setting", "manuallock", ischeck ? 1 : 0);
            reloadLockConfig(); 
        }

        private void ckb_manuclunlock_Click(object sender, EventArgs e)
        {
            var ischeck = ckb_manuclunlock.Checked;
            OperateIniFile.WriteIniInt("setting", "manualunlock", ischeck ? 1 : 0);
            reloadLockConfig();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            unlockManager.sessionSwitchClass.dolocking = true;
            unlockManager.sessionSwitchClass.isLockBySoft = true;
            WanClient.LockPc();  
        }

        private void 锁屏ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            unlockManager.sessionSwitchClass.dolocking = true;
            unlockManager.sessionSwitchClass.isLockBySoft = true;
            WanClient.LockPc();
        }

        private void 隐藏ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void btn_searchDevice_Click(object sender, EventArgs e)
        {
            BtDeviceListForm btDeviceListForm = new BtDeviceListForm(bletype);
            btDeviceListForm.StartPosition = FormStartPosition.CenterParent;
            btDeviceListForm.Owner = this;
            btDeviceListForm.ShowDialog(); 
            if(btDeviceListForm.DialogResult == DialogResult.OK)
            {
                bletype = btDeviceListForm.bletype;
                unlockManager.bletype = btDeviceListForm.bletype; 
                OperateIniFile.WriteIniInt("setting", "type", bletype); 
                unlockManager.setunlockaddress(btDeviceListForm.address);
                OperateIniFile.WriteSafeString("setting", "address", btDeviceListForm.address); 
                txt_bleDevice.Text =  btDeviceListForm.address;
                getBluetoothType();
                unlockManager.Stop();
                unlockManager.Start();

            }
        }
    }
}
