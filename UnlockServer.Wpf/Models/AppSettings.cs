using System.Collections.Generic;
using UnlockServer.ViewModels;

namespace UnlockServer.Models
{
    /// <summary>
    /// 应用设置模型
    /// </summary>
    public class AppSettings : ViewModelBase
    {
        private string _serverIp = "127.0.0.1";
        private int _serverPort = 2084;
        private string _username = "";
        private string _password = "";
        private int _rssiThreshold = -70;
        private string _deviceAddress = "";
        private int _bluetoothType = 2; // 1=Classic, 2=BLE
        private bool _autoLock = false;
        private bool _autoUnlock = false;
        private bool _manualLock = true;
        private bool _manualUnlock = false;
        private bool _autoStart = false;
        private int _lockDelay = 15;
        private int _unlockDelay = 3;
        private int _hysteresisDb = 8;
        private int _presenceTimeout = 8;
        private bool _requireAllDevices;
        private bool _useLocalUnlock = true;
        private List<BoundDevice> _devices = new List<BoundDevice>();

        /// <summary>
        /// 服务器IP
        /// </summary>
        public string ServerIp
        {
            get => _serverIp;
            set => SetProperty(ref _serverIp, value);
        }

        /// <summary>
        /// 服务器端口
        /// </summary>
        public int ServerPort
        {
            get => _serverPort;
            set => SetProperty(ref _serverPort, value);
        }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        /// <summary>
        /// 密码
        /// </summary>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        /// <summary>
        /// 信号阈值
        /// </summary>
        public int RssiThreshold
        {
            get => _rssiThreshold;
            set => SetProperty(ref _rssiThreshold, value);
        }

        /// <summary>
        /// 绑定的设备地址
        /// </summary>
        public string DeviceAddress
        {
            get => _deviceAddress;
            set => SetProperty(ref _deviceAddress, value);
        }

        /// <summary>
        /// 蓝牙类型 1=经典蓝牙 2=BLE
        /// </summary>
        public int BluetoothType
        {
            get => _bluetoothType;
            set => SetProperty(ref _bluetoothType, value);
        }

        /// <summary>
        /// 是否自动锁定
        /// </summary>
        public bool AutoLock
        {
            get => _autoLock;
            set => SetProperty(ref _autoLock, value);
        }

        /// <summary>
        /// 是否自动解锁
        /// </summary>
        public bool AutoUnlock
        {
            get => _autoUnlock;
            set => SetProperty(ref _autoUnlock, value);
        }

        /// <summary>
        /// 不干预手动锁定
        /// </summary>
        public bool ManualLock
        {
            get => _manualLock;
            set => SetProperty(ref _manualLock, value);
        }

        /// <summary>
        /// 不干预手动解锁
        /// </summary>
        public bool ManualUnlock
        {
            get => _manualUnlock;
            set => SetProperty(ref _manualUnlock, value);
        }

        /// <summary>
        /// 开机自启
        /// </summary>
        public bool AutoStart
        {
            get => _autoStart;
            set => SetProperty(ref _autoStart, value);
        }

        /// <summary>
        /// 锁定延迟（秒）- 设备离开后多少秒锁定
        /// </summary>
        public int LockDelay
        {
            get => _lockDelay;
            set => SetProperty(ref _lockDelay, value);
        }

        /// <summary>
        /// 解锁延迟（秒）- 设备进入后多少秒解锁
        /// </summary>
        public int UnlockDelay
        {
            get => _unlockDelay;
            set => SetProperty(ref _unlockDelay, value);
        }

        /// <summary>
        /// 滞后区间（dB）。进入范围要比离开更近，减少临界抖动。
        /// </summary>
        public int HysteresisDb
        {
            get => _hysteresisDb;
            set => SetProperty(ref _hysteresisDb, value);
        }

        /// <summary>
        /// 信号丢失超时（秒）。超过该时间未见新广播则视为离开。
        /// </summary>
        public int PresenceTimeout
        {
            get => _presenceTimeout;
            set => SetProperty(ref _presenceTimeout, value);
        }

        /// <summary>true=全部在附近才解锁；false=任一设备即可</summary>
        public bool RequireAllDevices
        {
            get => _requireAllDevices;
            set => SetProperty(ref _requireAllDevices, value);
        }

        public bool UseLocalUnlock
        {
            get => _useLocalUnlock;
            set => SetProperty(ref _useLocalUnlock, value);
        }

        public List<BoundDevice> Devices
        {
            get => _devices;
            set => SetProperty(ref _devices, value ?? new List<BoundDevice>());
        }
    }
}

