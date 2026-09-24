using System;
using System.Collections.ObjectModel;
using System.Linq;
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
        private bool _disposed;

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
                    FilterDevices();
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
            _devices = new ObservableCollection<BluetoothDeviceModel>();
            _bluetoothType = initialBluetoothType;
            _selectedAddress = initialAddress;

            _bluetoothService = BluetoothService.Shared;
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
            if (_disposed || IsScanning) return;
            try
            {
                _bluetoothService.AddScanUser(discovery: true);
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
            if (!IsScanning) return;
            try
            {
                _refreshTimer.Stop();
                IsScanning = false;
                _bluetoothService.RemoveScanUser(discovery: true);
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

            var device = _bluetoothService.AddManualDevice(ManualAddress, type: _bluetoothType);
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

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshDeviceList();
        }

        #endregion

        #region 私有方法

        private void RestartScanning()
        {
            Devices.Clear();
            if (_bluetoothService.IsScanning)
                _bluetoothService.RestartWatchers("device list refresh");
            else
                StartScanning();
        }

        private void RefreshDeviceList()
        {
            if (_disposed) return;
            var allDevices = _bluetoothService.GetDevices().Where(MatchesSearch).ToList();
            var addresses = new System.Collections.Generic.HashSet<string>(
                allDevices.Select(d => d.Address), StringComparer.OrdinalIgnoreCase);
            foreach (var old in Devices.Where(d => !addresses.Contains(d.Address)).ToList())
                Devices.Remove(old);

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
                existing.IsConnected = device.IsConnected;
                if (device.IsPaired)
                    existing.IsPaired = true;
            }
            else
            {
                // 检查是否符合搜索条件
                if (MatchesSearch(device))
                {
                    // UI owns its models; scanner updates never raise WPF notifications.
                    Devices.Add(device.Copy());
                    OnPropertyChanged(nameof(ShowScanningHint));
                }
            }
        }

        private void FilterDevices()
        {
            RefreshDeviceList();
            OnPropertyChanged(nameof(ShowScanningHint));
        }

        private bool MatchesSearch(BluetoothDeviceModel device)
        {
            if (_bluetoothType == 1 && device.Type != "Classic") return false;
            if (_bluetoothType == 2 && device.Type != "BLE") return false;
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
            if (_disposed) return;
            StopScanning();
            _disposed = true;
            _refreshTimer.Tick -= RefreshTimer_Tick;
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

