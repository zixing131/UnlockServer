using System;
using System.Windows;
using System.Windows.Input;
using UnlockServer.Models;
using UnlockServer.Services;
using UnlockServer.Views;

namespace UnlockServer.ViewModels
{
    /// <summary>
    /// 主窗口视图模型
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        #region 字段

        private readonly ConfigService _configService;
        private UnlockManager _unlockManager;
        private AppSettings _settings;
        private string _statusText = "就绪";
        private string _currentRssi = "-- dBm";
        private bool _isDeviceInRange = false;
        private bool _isConnected;
        private bool _isMonitoring;
        private string _deviceDisplayName = "未选择设备";

        #endregion

        #region 属性

        public AppSettings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string CurrentRssi
        {
            get => _currentRssi;
            set => SetProperty(ref _currentRssi, value);
        }

        /// <summary>
        /// 设备是否在范围内（用于颜色显示）
        /// </summary>
        public bool IsDeviceInRange
        {
            get => _isDeviceInRange;
            set => SetProperty(ref _isDeviceInRange, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set => SetProperty(ref _isMonitoring, value);
        }

        public string DeviceDisplayName
        {
            get => _deviceDisplayName;
            set => SetProperty(ref _deviceDisplayName, value);
        }

        public bool IsClassicBluetooth
        {
            get => Settings?.BluetoothType == 1;
            set
            {
                if (value && Settings != null)
                {
                    Settings.BluetoothType = 1;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsBLE));
                }
            }
        }

        public bool IsBLE
        {
            get => Settings?.BluetoothType == 2;
            set
            {
                if (value && Settings != null)
                {
                    Settings.BluetoothType = 2;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsClassicBluetooth));
                }
            }
        }

        #endregion

        #region 命令

        public ICommand SaveSettingsCommand { get; }
        public ICommand SearchDeviceCommand { get; }
        public ICommand LockScreenCommand { get; }
        public ICommand ToggleMonitoringCommand { get; }
        public ICommand ShowWindowCommand { get; }
        public ICommand ExitCommand { get; }

        #endregion

        #region 构造函数

        public MainViewModel()
        {
            _configService = ConfigService.Instance;
            _settings = _configService.LoadSettings();

            // 初始化命令
            SaveSettingsCommand = new RelayCommand(SaveSettings, CanSaveSettings);
            SearchDeviceCommand = new RelayCommand(SearchDevice);
            LockScreenCommand = new RelayCommand(LockScreen);
            ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
            ShowWindowCommand = new RelayCommand(ShowWindow);
            ExitCommand = new RelayCommand(ExitApplication);

            // 更新设备显示名称
            UpdateDeviceDisplayName();

            // 初始化解锁管理器
            InitializeUnlockManager();
        }

        #endregion

        #region 命令实现

        private bool CanSaveSettings(object parameter)
        {
            return Settings != null;
        }

        private void SaveSettings(object parameter)
        {
            try
            {
                // 验证输入
                if (string.IsNullOrEmpty(Settings.ServerIp))
                {
                    MessageDialog.ShowWarning("IP地址不能为空！");
                    return;
                }

                if (string.IsNullOrEmpty(Settings.Username))
                {
                    MessageDialog.ShowWarning("账号不能为空！");
                    return;
                }

                if (string.IsNullOrEmpty(Settings.Password))
                {
                    MessageDialog.ShowWarning("密码不能为空！");
                    return;
                }

                if (Settings.RssiThreshold > 0 || Settings.RssiThreshold < -128)
                {
                    MessageDialog.ShowWarning("请输入有效的信号阈值(-128到0)！");
                    return;
                }

                if (Settings.ServerPort <= 0 || Settings.ServerPort >= 65535)
                {
                    MessageDialog.ShowWarning("请输入有效的端口号(1到65534)！");
                    return;
                }

                if (_configService.SaveSettings(Settings))
                {
                    // 重新加载远程配置
                    WanClient.reloadConfig();

                    // 更新解锁管理器配置
                    if (_unlockManager != null)
                    {
                        _unlockManager.rssiyuzhi = Settings.RssiThreshold;
                        _unlockManager.bletype = Settings.BluetoothType;
                        _unlockManager.isautolock = Settings.AutoLock;
                        _unlockManager.isautounlock = Settings.AutoUnlock;
                        _unlockManager.manuallock = Settings.ManualLock;
                        _unlockManager.manualunlock = Settings.ManualUnlock;
                        _unlockManager.lockDelay = Settings.LockDelay;
                        _unlockManager.unlockDelay = Settings.UnlockDelay;
                    }

                    MessageDialog.ShowSuccess("设置已保存！");
                }
                else
                {
                    MessageDialog.ShowError("保存失败！");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"保存设置失败: {ex.Message}");
                MessageDialog.ShowError("保存配置发生错误！");
            }
        }

        private void SearchDevice(object parameter)
        {
            // 通过事件通知 View 打开设备列表窗口
            OnRequestSearchDevice?.Invoke(this, Settings.BluetoothType);
        }

        private void LockScreen(object parameter)
        {
            try
            {
                if (_unlockManager?.sessionSwitchClass != null)
                {
                    _unlockManager.sessionSwitchClass.dolocking = true;
                    _unlockManager.sessionSwitchClass.isLockBySoft = true;
                }
                WanClient.LockPc();
                StatusText = "已锁定";
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"锁屏失败: {ex.Message}");
            }
        }

        private void ToggleMonitoring(object parameter)
        {
            if (IsMonitoring)
            {
                StopMonitoring();
            }
            else
            {
                StartMonitoring();
            }
        }

        private void ShowWindow(object parameter)
        {
            OnRequestShowWindow?.Invoke(this, EventArgs.Empty);
        }

        private void ExitApplication(object parameter)
        {
            if (MessageDialog.ShowConfirm("是否退出程序？"))
            {
                StopMonitoring();
                OnRequestExit?.Invoke(this, EventArgs.Empty);
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置选中的设备
        /// </summary>
        public void SetSelectedDevice(string address, int bluetoothType)
        {
            Settings.DeviceAddress = address;
            Settings.BluetoothType = bluetoothType;
            _configService.SaveDeviceAddress(address);
            _configService.SaveBluetoothType(bluetoothType);

            UpdateDeviceDisplayName();

            // 更新解锁管理器
            if (_unlockManager != null)
            {
                _unlockManager.setunlockaddress(address);
                _unlockManager.bletype = bluetoothType;
                
                // 重启监控
                if (IsMonitoring)
                {
                    _unlockManager.Stop();
                    _unlockManager.Start();
                }
            }

            OnPropertyChanged(nameof(IsClassicBluetooth));
            OnPropertyChanged(nameof(IsBLE));
        }

        /// <summary>
        /// 初始化（窗口加载时调用）
        /// </summary>
        public void Initialize()
        {
            // 启动后自动开始监控
            StartMonitoring();
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Cleanup()
        {
            StopMonitoring();
        }

        #endregion

        #region 私有方法

        private void InitializeUnlockManager()
        {
            try
            {
                _unlockManager = new UnlockManager
                {
                    bletype = Settings.BluetoothType,
                    rssiyuzhi = Settings.RssiThreshold,
                    isautolock = Settings.AutoLock,
                    isautounlock = Settings.AutoUnlock,
                    manuallock = Settings.ManualLock,
                    manualunlock = Settings.ManualUnlock,
                    lockDelay = Settings.LockDelay,
                    unlockDelay = Settings.UnlockDelay
                };

                _unlockManager.setunlockaddress(Settings.DeviceAddress);
                _unlockManager.UpdategRssi = UpdateRssiDisplay;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"初始化解锁管理器失败: {ex.Message}");
            }
        }

        private void StartMonitoring()
        {
            try
            {
                if (_unlockManager == null)
                {
                    InitializeUnlockManager();
                }

                _unlockManager?.Start();
                IsMonitoring = true;
                StatusText = "监控中...";
                IsConnected = true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"启动监控失败: {ex.Message}");
                MessageDialog.ShowError("启动蓝牙监控失败，可能没有蓝牙硬件或者不兼容！");
            }
        }

        private void StopMonitoring()
        {
            try
            {
                _unlockManager?.Stop();
                IsMonitoring = false;
                StatusText = "已停止";
                IsConnected = false;
                CurrentRssi = "-- dBm";
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"停止监控失败: {ex.Message}");
            }
        }

        private void UpdateRssiDisplay(string displayText, bool isRealRssi)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                // 直接显示传入的文本（已经格式化好了）
                CurrentRssi = displayText;
                
                // 判断是否在范围内（用于颜色显示）
                // 真实 RSSI 值 或 包含"在范围内" 表示设备在范围
                IsDeviceInRange = isRealRssi || displayText.Contains("在范围内");
            });
        }

        private void UpdateDeviceDisplayName()
        {
            if (string.IsNullOrEmpty(Settings?.DeviceAddress))
            {
                DeviceDisplayName = "未选择设备";
            }
            else
            {
                DeviceDisplayName = Settings.DeviceAddress;
            }
        }

        #endregion

        #region 事件

        public event EventHandler<int> OnRequestSearchDevice;
        public event EventHandler OnRequestShowWindow;
        public event EventHandler OnRequestExit;

        #endregion
    }
}

