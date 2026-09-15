using System;
using System.Runtime.Serialization;
using UnlockServer.ViewModels;

namespace UnlockServer.Models
{
    [DataContract]
    public class BoundDevice : ViewModelBase
    {
        private string _name;
        private string _address;
        private int _bluetoothType = 2;
        private bool _enabled = true;
        private short _rssi = -100;
        private bool _isInRange;
        private string _statusText = "未检测到";

        [DataMember]
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                    OnPropertyChanged(nameof(DisplayName));
            }
        }

        [DataMember]
        public string Address
        {
            get => _address;
            set => SetProperty(ref _address, value);
        }

        [DataMember]
        public int BluetoothType
        {
            get => _bluetoothType;
            set
            {
                if (SetProperty(ref _bluetoothType, value))
                    OnPropertyChanged(nameof(TypeLabel));
            }
        }

        [DataMember]
        public bool Enabled
        {
            get => _enabled;
            set => SetProperty(ref _enabled, value);
        }

        public short Rssi
        {
            get => _rssi;
            set
            {
                if (SetProperty(ref _rssi, value))
                    OnPropertyChanged(nameof(RssiDisplay));
            }
        }

        public bool IsInRange
        {
            get => _isInRange;
            set => SetProperty(ref _isInRange, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (Address ?? "未命名") : Name;

        public string TypeLabel => BluetoothType == 1 ? "经典" : "BLE";

        public string RssiDisplay => Rssi > -100 ? $"{Rssi} dBm" : "--";

        public string NormalizedAddress => BluetoothDiscover.NormalizeAddress(Address) ?? Address;
    }
}
