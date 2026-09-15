using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UnlockServer.Models;
using UnlockServer.Services;
using UnlockServer.Views;
using UnlockServer;

namespace UnlockServer.ViewModels
{
    /// <summary>
    /// 设备列表视图模型
    /// </summary>
    public class DeviceListViewModel : ViewModelBase, IDisposable
    {
        #region 字段

        private readonly BluetoothService _bluetoothService;
        private readonly DispatcherTimer _refreshTimer;
        private readonly SynchronizationContext _syncContext;

        private ObservableCollection<BluetoothDeviceModel> _devices;
        private BluetoothDeviceModel _selectedDevice;
        private int _bluetoothType = 2;
        private bool _isScanning;
        private string _searchText = "";
        private string _selectedAddress;
        private string _manualAddress = "";
        private string _scanHint = "手表/手环搜不到时：断开与手机的蓝牙，打开可被发现或心率广播，也可手动输入 MAC。";

        // 排序防抖
        private DateTime _lastSortTime = DateTime.MinValue;
        private int _lastDeviceCount = 0;
        private const int SortIntervalSeconds = 10;

        #endregion

        #region 属性

        public ObservableCollection<BluetoothDeviceModel> Devices
        {
            get => _devices;
            set => SetProperty(ref _devices, value);
        }

        public BluetoothDeviceModel SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetProperty(ref _selectedDevice, value))
                {
                    _selectedAddress = value?.Address;
                }
            }
        }

        public int BluetoothType
        {
            get => _bluetoothType;
            set
            {
                if (SetProperty(ref _bluetoothType, value))
                {
                    OnPropertyChanged(nameof(IsClassicBluetooth));
                    OnPropertyChanged(nameof(IsBLE));
                    OnPropertyChanged(nameof(IsAllBluetooth));
                    RestartScanning();
                }
            }
        }

        public bool IsClassicBluetooth
        {
            get => _bluetoothType == 1;
            set
            {
                if (value) BluetoothType = 1;
            }
        }

        public bool IsBLE
        {
            get => _bluetoothType == 2;
            set
            {
                if (value) BluetoothType = 2;
            }
        }

        public bool IsAllBluetooth
        {
            get => _bluetoothType == 0;
            set
            {
                if (value) BluetoothType = 0;
            }
        }

        public string ManualAddress
        {
            get => _manualAddress;
            set => SetProperty(ref _manualAddress, value);
        }

        public string ScanHint
        {
            get => _scanHint;
            set => SetProperty(ref _scanHint, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            set
            {
                if (SetProperty(ref _isScanning, value))
                {
                    OnPropertyChanged(nameof(ShowScanningHint));
                }
            }
        }

        /// <summary>
        /// 是否显示扫描提示（扫描中且列表为空）
        /// </summary>
        public bool ShowScanningHint => _isScanning && (Devices == null || Devices.Count == 0);

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterDevices();
                }
            }
        }

        public string SelectedAddress
        {
            get => _selectedAddress;
            set => SetProperty(ref _selectedAddress, value);
        }

        #endregion

        #region 命令

        public ICommand SelectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand UseManualAddressCommand { get; }

        #endregion

        #region 构造函数

        public DeviceListViewModel(int initialBluetoothType = 1, string initialAddress = "")
        {
            _syncContext = SynchronizationContext.Current;
            _devices = new ObservableCollection<BluetoothDeviceModel>();
            _bluetoothType = initialBluetoothType;
            _selectedAddress = initialAddress;

            _bluetoothService = BluetoothService.Shared;
            _bluetoothService.BluetoothType = _bluetoothType;
            if (!string.IsNullOrEmpty(_selectedAddress))
                _bluetoothService.PinAddress(_selectedAddress);
            _bluetoothService.DeviceDiscovered += BluetoothService_DeviceDiscovered;
            _bluetoothService.DeviceUpdated += BluetoothService_DeviceUpdated;
            _bluetoothService.DeviceLost += BluetoothService_DeviceLost;

            // 初始化定时器
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // 初始化命令
            SelectCommand = new RelayCommand(SelectDevice, CanSelectDevice);
            RefreshCommand = new RelayCommand(_ => RestartScanning());
            UseManualAddressCommand = new RelayCommand(UseManualAddress);
        }

        #endregion

        #region 公共方法

        public void StartScanning()
        {
            try
            {
                _bluetoothService.BluetoothType = _bluetoothType;
                _bluetoothService.AddScanUser();
                _refreshTimer.Start();
                IsScanning = true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动扫描失败: {ex.Message}");
                MessageDialog.ShowError("启动蓝牙扫描失败！");
            }
        }

        public void StopScanning()
        {
            try
            {
                _refreshTimer.Stop();
                _bluetoothService.RemoveScanUser();
                IsScanning = false;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"停止扫描失败: {ex.Message}");
            }
        }

        #endregion

        #region 命令实现

        private bool CanSelectDevice(object parameter)
        {
            return SelectedDevice != null;
        }

        private void SelectDevice(object parameter)
        {
            if (SelectedDevice == null)
            {
                MessageDialog.ShowWarning("请选择一个设备！");
                return;
            }

            // 通过事件通知选择完成
            var type = SelectedDevice.Type == "BLE" ? 2 : 1;
            if (_bluetoothType == 1 || _bluetoothType == 2)
                type = _bluetoothType;

            OnDeviceSelected?.Invoke(this, new DeviceSelectedEventArgs
            {
                Address = $"{SelectedDevice.DisplayName}[{SelectedDevice.Address}]",
                BluetoothType = type
            });
        }

        private void UseManualAddress(object parameter)
        {
            if (!UnlockManager.IsValidBluetoothAddress(ManualAddress))
            {
                MessageDialog.ShowWarning("请输入有效的 MAC 地址，例如 AA:BB:CC:DD:EE:FF");
                return;
            }

            var device = _bluetoothService.AddManualDevice(ManualAddress);
            if (device == null)
            {
                MessageDialog.ShowError("无法添加该地址");
                return;
            }

            AddOrUpdateDevice(device);
            SelectedDevice = Devices.FirstOrDefault(d =>
                d.Address.Equals(device.Address, StringComparison.OrdinalIgnoreCase));
            SelectDevice(null);
        }

        #endregion

        #region 事件处理

        private void BluetoothService_DeviceDiscovered(object sender, BluetoothDeviceModel device)
        {
            _syncContext?.Post(_ => AddOrUpdateDevice(device), null);
        }

        private void BluetoothService_DeviceUpdated(object sender, BluetoothDeviceModel device)
        {
            _syncContext?.Post(_ => UpdateDevice(device), null);
        }

        private void BluetoothService_DeviceLost(object sender, string address)
        {
            _syncContext?.Post(_ => RemoveDevice(address), null);
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshDeviceList();
        }

        #endregion

        #region 私有方法

        private void RestartScanning()
        {
            Devices.Clear();
            _bluetoothService.BluetoothType = _bluetoothType;
            if (_bluetoothService.IsScanning)
                _bluetoothService.RestartWatchers("device list refresh");
            else
                StartScanning();
        }

        private void RefreshDeviceList()
        {
            var allDevices = _bluetoothService.GetDevices();
            
            foreach (var device in allDevices)
            {
                AddOrUpdateDevice(device);
            }

            // 排序防抖：只在设备数量变化或超过10秒后才重新排序
            var now = DateTime.Now;
            bool shouldSort = Devices.Count != _lastDeviceCount || 
                              (now - _lastSortTime).TotalSeconds >= SortIntervalSeconds;

            if (shouldSort)
            {
                // 按配对状态和信号强度排序
                var sorted = Devices.OrderByDescending(d => d.IsPaired).ThenByDescending(d => d.Rssi).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var currentIndex = Devices.IndexOf(sorted[i]);
                    if (currentIndex != i)
                    {
                        Devices.Move(currentIndex, i);
                    }
                }

                _lastSortTime = now;
                _lastDeviceCount = Devices.Count;
            }

            // 恢复选中状态
            if (!string.IsNullOrEmpty(_selectedAddress) && SelectedDevice == null)
            {
                var device = Devices.FirstOrDefault(d => 
                    d.Address.Equals(_selectedAddress, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    SelectedDevice = device;
                }
            }
        }

        private void AddOrUpdateDevice(BluetoothDeviceModel device)
        {
            var existing = Devices.FirstOrDefault(d => 
                d.Address.Equals(device.Address, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (!string.IsNullOrEmpty(device.Name))
                    existing.Name = device.Name;
                existing.Rssi = device.Rssi;
                existing.Type = device.Type;
                existing.LastSeen = device.LastSeen;
                if (device.IsPaired)
                    existing.IsPaired = true;
            }
            else
            {
                // 检查是否符合搜索条件
                if (MatchesSearch(device))
                {
                    Devices.Add(device);
                    OnPropertyChanged(nameof(ShowScanningHint));
                }
            }
        }

        private void UpdateDevice(BluetoothDeviceModel device)
        {
            var existing = Devices.FirstOrDefault(d => 
                d.Address.Equals(device.Address, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Rssi = device.Rssi;
                existing.LastSeen = device.LastSeen;
            }
        }

        private void RemoveDevice(string address)
        {
            var device = Devices.FirstOrDefault(d => 
                d.Address.Equals(address, StringComparison.OrdinalIgnoreCase));

            if (device != null)
            {
                Devices.Remove(device);
            }
        }

        private void FilterDevices()
        {
            var allDevices = _bluetoothService.GetDevices();
            Devices.Clear();

            foreach (var device in allDevices.Where(MatchesSearch))
            {
                Devices.Add(device);
            }
        }

        private bool MatchesSearch(BluetoothDeviceModel device)
        {
            if (string.IsNullOrWhiteSpace(_searchText))
                return true;

            var searchLower = _searchText.ToLower();
            return (device.Name?.ToLower().Contains(searchLower) ?? false) ||
                   (device.Address?.ToLower().Contains(searchLower) ?? false);
        }

        #endregion

        #region 事件

        public event EventHandler<DeviceSelectedEventArgs> OnDeviceSelected;

        #endregion

        #region IDisposable

        public void Dispose()
        {
            StopScanning();
            _bluetoothService.DeviceDiscovered -= BluetoothService_DeviceDiscovered;
            _bluetoothService.DeviceUpdated -= BluetoothService_DeviceUpdated;
            _bluetoothService.DeviceLost -= BluetoothService_DeviceLost;
        }

        #endregion
    }

    /// <summary>
    /// 设备选择事件参数
    /// </summary>
    public class DeviceSelectedEventArgs : EventArgs
    {
        public string Address { get; set; }
        public int BluetoothType { get; set; }
    }
}

