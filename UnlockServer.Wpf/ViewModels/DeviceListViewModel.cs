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
        private int _bluetoothType = 1;
        private bool _isScanning;
        private string _searchText = "";
        private string _selectedAddress;

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

        #endregion

        #region 构造函数

        public DeviceListViewModel(int initialBluetoothType = 1, string initialAddress = "")
        {
            _syncContext = SynchronizationContext.Current;
            _devices = new ObservableCollection<BluetoothDeviceModel>();
            _bluetoothType = initialBluetoothType;
            _selectedAddress = initialAddress;

            // 初始化蓝牙服务
            _bluetoothService = new BluetoothService();
            _bluetoothService.BluetoothType = _bluetoothType;
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
        }

        #endregion

        #region 公共方法

        public void StartScanning()
        {
            try
            {
                _bluetoothService.BluetoothType = _bluetoothType;
                _bluetoothService.StartScan();
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
                _bluetoothService.StopScan();
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
            OnDeviceSelected?.Invoke(this, new DeviceSelectedEventArgs
            {
                Address = $"{SelectedDevice.DisplayName}[{SelectedDevice.Address}]",
                BluetoothType = _bluetoothType
            });
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
            StopScanning();
            Devices.Clear();
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
            _bluetoothService.Dispose();
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

