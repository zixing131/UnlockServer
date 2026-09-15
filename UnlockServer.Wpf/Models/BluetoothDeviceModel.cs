using System;
using UnlockServer.ViewModels;

namespace UnlockServer.Models
{
    /// <summary>
    /// 蓝牙设备模型（带属性通知）
    /// </summary>
    public class BluetoothDeviceModel : ViewModelBase
    {
        private string _name;
        private string _address;
        private short _rssi;
        private string _type;
        private bool _isSelected;
        private bool _isPaired;
        private bool _isConnected;
        private DateTime _lastSeen;

        /// <summary>
        /// 设备名称
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// 设备地址（MAC）
        /// </summary>
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        /// <summary>
        /// 信号强度
        /// </summary>
        public short Rssi
        {
            get => _rssi;
            set
            {
                if (SetProperty(ref _rssi, value))
                {
                    OnPropertyChanged(nameof(RssiDisplay));
                    OnPropertyChanged(nameof(SignalLevel));
                }
            }
        }

        /// <summary>
        /// 设备类型（BLE/Classic）
        /// </summary>
        public string Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// 是否已配对
        /// </summary>
        public bool IsPaired
        {
            get => _isPaired;
            set => SetProperty(ref _isPaired, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        /// <summary>
        /// 最后发现时间
        /// </summary>
        public DateTime LastSeen
        {
            get => _lastSeen;
            set => SetProperty(ref _lastSeen, value);
        }

        /// <summary>
        /// RSSI 显示文本
        /// </summary>
        public string RssiDisplay => $"{Rssi} dBm";

        /// <summary>
        /// 信号等级（0-4）
        /// </summary>
        public int SignalLevel
        {
            get
            {
                if (Rssi >= -50) return 4;
                if (Rssi >= -60) return 3;
                if (Rssi >= -70) return 2;
                if (Rssi >= -80) return 1;
                return 0;
            }
        }

        /// <summary>
        /// 显示名称（优先设备名，无则显示地址）
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(Name) ? Address : Name;
    }
}

