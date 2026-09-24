using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UnlockServer.Models;
using UnlockServer.Services;
using UnlockServer.Views;

namespace UnlockServer.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ConfigService _configService;
        private UnlockManager _unlockManager;
        private AppSettings _settings;
        private string _statusText = "就绪";
        private string _currentRssi = "--";
        private bool _isDeviceInRange;
        private bool _isConnected;
        private bool _isMonitoring;
        private int _selectedTab;
        private string _localUnlockStatus = "尚未安装本机解锁组件";
        private readonly DispatcherTimer _autoSaveTimer;
        private volatile int _monitorVersion;
        private bool _suspendAutoSave;

        public ObservableCollection<BoundDevice> BoundDevices { get; } = new ObservableCollection<BoundDevice>();

        public string AppVersion { get; } = FormatAppVersion();

        public string WindowTitle => "蓝牙解锁 " + AppVersion;

        private static string FormatAppVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null ? "" : "v" + version.ToString(3);
        }

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

        public int SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value))
                {
                    OnPropertyChanged(nameof(IsTabDevices));
                    OnPropertyChanged(nameof(IsTabUnlock));
                    OnPropertyChanged(nameof(IsTabRules));
                }
            }
        }

        public bool IsTabDevices => SelectedTab == 0;
        public bool IsTabUnlock => SelectedTab == 1;
        public bool IsTabRules => SelectedTab == 2;

        public bool HasNoDevices => BoundDevices.Count == 0;

        public bool HasMultipleDevices => BoundDevices.Count > 1;

        public string InstallButtonText => LocalUnlock.IsProviderRegistered() ? "更新密码" : "启用本机解锁";

        public string SetupHint
        {
            get
            {
                if (BoundDevices.Count == 0)
                    return "下一步：添加手机、手表或鼠标，靠近时解锁，离开时锁屏。";
                if (!LocalUnlock.CanUnlock())
                    return "下一步：到「解锁」页填写 Windows 登录密码（不是 PIN），再点启用。";
                if (!Settings.AutoLock && !Settings.AutoUnlock)
                    return "下一步：到「规则」页打开自动锁屏或自动解锁。";
                return "";
            }
        }

        public bool HasSetupHint => !string.IsNullOrEmpty(SetupHint);

        public string LocalUnlockStatus
        {
            get => _localUnlockStatus;
            set => SetProperty(ref _localUnlockStatus, value);
        }

        public bool IsLocalUnlockReady => LocalUnlock.CanUnlock();

        public bool CanUninstallLocalUnlock =>
            LocalUnlock.IsProviderRegistered() || LocalUnlock.HasSavedCredential();

        public bool CanTestUnlock => LocalUnlock.IsProviderRegistered();

        public ICommand SearchDeviceCommand { get; }
        public ICommand LockScreenCommand { get; }
        public ICommand ToggleMonitoringCommand { get; }
        public ICommand ShowWindowCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand TestServerCommand { get; }
        public ICommand SelectTabCommand { get; }
        public ICommand RemoveDeviceCommand { get; }
        public ICommand InstallLocalUnlockCommand { get; }
        public ICommand UninstallLocalUnlockCommand { get; }
        public ICommand TestUnlockCommand { get; }

        public MainViewModel()
        {
            _configService = ConfigService.Instance;
            _settings = _configService.LoadSettings();

            if (string.IsNullOrEmpty(_settings.Username))
                _settings.Username = Environment.UserName;

            foreach (var d in _settings.Devices ?? Enumerable.Empty<BoundDevice>())
            {
                AttachDevice(d);
                BoundDevices.Add(d);
            }

            BoundDevices.CollectionChanged += OnBoundDevicesChanged;

            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                PersistSettings(false);
            };
            Settings.PropertyChanged += SettingsOnPropertyChanged;

            SearchDeviceCommand = new RelayCommand(SearchDevice);
            LockScreenCommand = new RelayCommand(LockScreen);
            ToggleMonitoringCommand = new RelayCommand(ToggleMonitoring);
            ShowWindowCommand = new RelayCommand(ShowWindow);
            ExitCommand = new RelayCommand(ExitApplication);
            TestServerCommand = new RelayCommand(TestServer);
            SelectTabCommand = new RelayCommand(p =>
            {
                if (p != null && int.TryParse(p.ToString(), out var tab))
                    SelectedTab = tab;
            });
            RemoveDeviceCommand = new RelayCommand(RemoveDevice);
            InstallLocalUnlockCommand = new RelayCommand(InstallLocalUnlock);
            UninstallLocalUnlockCommand = new RelayCommand(UninstallLocalUnlock);
            TestUnlockCommand = new RelayCommand(TestUnlock, _ => CanTestUnlock);

            RefreshLocalUnlockStatus();
            InitializeUnlockManager();
        }

        private void SettingsOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_suspendAutoSave) return;
            if (e.PropertyName == nameof(AppSettings.Devices)) return;
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private void PersistSettings(bool showError)
        {
            try
            {
                _suspendAutoSave = true;
                Settings.Devices = BoundDevices.ToList();
                if (!_configService.SaveSettings(Settings))
                {
                    if (showError) MessageDialog.ShowError("保存失败");
                    return;
                }

                WanClient.reloadConfig();
                if (Settings.UseLocalUnlock &&
                    !string.IsNullOrEmpty(Settings.Username) &&
                    !string.IsNullOrEmpty(Settings.Password))
                {
                    LocalUnlock.SaveCredential(Settings.Username, Settings.Password);
                }

                ApplySettingsToManager();
                RefreshLocalUnlockStatus();
                RefreshSetupHint();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"保存设置失败: {ex.Message}");
                if (showError) MessageDialog.ShowError("保存配置发生错误");
            }
            finally
            {
                _suspendAutoSave = false;
            }
        }

        private void TestServer(object parameter)
        {
            try
            {
                _configService.SaveSettings(Settings);
                WanClient.reloadConfig();
                if (WanClient.TestServer(out var message))
                    MessageDialog.ShowSuccess(message);
                else
                    MessageDialog.ShowWarning(message);
            }
            catch (Exception ex)
            {
                MessageDialog.ShowError("测试连接失败: " + ex.Message);
            }
        }

        private void InstallLocalUnlock(object parameter)
        {
            try
            {
                if (string.IsNullOrEmpty(Settings.Username) || string.IsNullOrEmpty(Settings.Password))
                {
                    MessageDialog.ShowWarning("请先填写 Windows 用户名和密码，再安装本机解锁。");
                    return;
                }

                PersistSettings(false);
                LocalUnlock.SaveCredential(Settings.Username, Settings.Password);

                if (LocalUnlock.IsProviderRegistered())
                {
                    RefreshLocalUnlockStatus();
                    MessageDialog.ShowSuccess("本机解锁凭据已更新。");
                    return;
                }

                if (!MessageDialog.ShowConfirm("将安装本机解锁组件，锁屏后可由本机直接解锁。是否继续？"))
                    return;

                if (LocalUnlock.RelaunchElevatedToInstall())
                {
                    RefreshLocalUnlockStatus();
                    MessageDialog.ShowSuccess("本机解锁已启用。之后靠近绑定设备即可自动解锁，无需远程服务。");
                }
                else
                {
                    RefreshLocalUnlockStatus();
                    MessageDialog.ShowWarning("安装未完成。也可继续使用远程解锁服务作为备用。");
                }
            }
            catch (Exception ex)
            {
                MessageDialog.ShowError("安装失败: " + ex.Message);
            }
        }

        private void UninstallLocalUnlock(object parameter)
        {
            try
            {
                if (!CanUninstallLocalUnlock)
                {
                    MessageDialog.ShowWarning("尚未安装本机解锁组件。");
                    return;
                }

                if (!MessageDialog.ShowConfirm("将卸载锁屏解锁组件，并删除已保存的 Windows 密码。锁屏后需用密码或 PIN 登录。是否继续？"))
                    return;

                var ok = LocalUnlock.RelaunchElevatedToUninstall();
                Settings.UseLocalUnlock = false;
                PersistSettings(false);
                RefreshLocalUnlockStatus();

                if (ok || !LocalUnlock.IsProviderRegistered())
                    MessageDialog.ShowSuccess("本机解锁已卸载。锁屏图标会在下次锁屏后消失。");
                else
                    MessageDialog.ShowWarning("卸载未完成。可再试一次，或锁屏一次后再卸载。");
            }
            catch (Exception ex)
            {
                MessageDialog.ShowError("卸载失败: " + ex.Message);
            }
        }

        private void TestUnlock(object parameter)
        {
            try
            {
                if (!MessageDialog.ShowConfirm("将会锁屏，然后约 5 秒后自动解锁。若解锁失败，请用密码或 PIN 手动登录。是否继续？"))
                    return;

                if (!string.IsNullOrEmpty(Settings.Username) && !string.IsNullOrEmpty(Settings.Password))
                {
                    PersistSettings(false);
                    LocalUnlock.SaveCredential(Settings.Username, Settings.Password);
                    RefreshLocalUnlockStatus();
                }

                if (_unlockManager == null)
                    InitializeUnlockManager();

                if (!_unlockManager.BeginUnlockTest(5, out var error))
                {
                    MessageDialog.ShowWarning(error);
                    return;
                }

                StatusText = "解锁测试中";
                _unlockManager.UnlockTestFinished = OnUnlockTestFinished;
            }
            catch (Exception ex)
            {
                MessageDialog.ShowError("无法开始解锁测试: " + ex.Message);
            }
        }

        private void OnUnlockTestFinished(bool ok)
        {
            if (IsMonitoring)
                StatusText = "监控中";
            else
                StatusText = ok ? "就绪" : "已锁定";
        }

        private void SearchDevice(object parameter)
        {
            OnRequestSearchDevice?.Invoke(this, 0);
        }

        private void OnBoundDevicesChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (BoundDevice d in e.NewItems)
                    AttachDevice(d);
            }
            if (e.OldItems != null)
            {
                foreach (BoundDevice d in e.OldItems)
                    DetachDevice(d);
            }
            OnPropertyChanged(nameof(HasNoDevices));
            RefreshSetupHint();
        }

        private void AttachDevice(BoundDevice device)
        {
            if (device == null) return;
            device.PropertyChanged -= BoundDeviceOnPropertyChanged;
            device.PropertyChanged += BoundDeviceOnPropertyChanged;
        }

        private void DetachDevice(BoundDevice device)
        {
            if (device == null) return;
            device.PropertyChanged -= BoundDeviceOnPropertyChanged;
        }

        private void BoundDeviceOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BoundDevice.Enabled)) return;
            Settings.Devices = BoundDevices.ToList();
            _configService.SaveSettings(Settings);
            SyncBoundDevicesToManager(false);
        }

        private void RemoveDevice(object parameter)
        {
            var device = parameter as BoundDevice;
            if (device == null) return;
            BoundDevices.Remove(device);
            Settings.Devices = BoundDevices.ToList();
            _configService.SaveSettings(Settings);
            SyncBoundDevicesToManager(false);
        }

        public void AddBoundDevice(string address, int bluetoothType)
        {
            var mac = BluetoothDiscover.NormalizeAddress(address);
            if (string.IsNullOrEmpty(mac)) return;

            var name = address ?? "";
            var start = name.LastIndexOf('[');
            if (start > 0) name = name.Substring(0, start);

            var existing = BoundDevices.FirstOrDefault(d =>
                mac.Equals(d.NormalizedAddress, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = name;
                existing.BluetoothType = bluetoothType;
                existing.Enabled = true;
            }
            else
            {
                BoundDevices.Add(new BoundDevice
                {
                    Name = name,
                    Address = mac,
                    BluetoothType = bluetoothType,
                    Enabled = true
                });
            }

            Settings.Devices = BoundDevices.ToList();
            _configService.SaveSettings(Settings);
            SyncBoundDevicesToManager(true);
            if (!IsMonitoring)
                StartMonitoring();
            RefreshSetupHint();

            if (!LocalUnlock.CanUnlock())
                ToastService.Show("已添加设备。请到「解锁」页填写 Windows 密码并启用本机解锁。");
            else if (!Settings.AutoLock && !Settings.AutoUnlock)
                ToastService.Show("已添加设备。可到「规则」页打开自动锁屏/解锁。");
            else
                ToastService.Show("已添加设备，正在监控。");
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
                _unlockManager?.MarkSoftwareLock();
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
                return;
            }

            if (BoundDevices.Count == 0)
            {
                SelectedTab = 0;
                MessageDialog.ShowWarning("请先添加一台蓝牙设备，再开始监控。");
                return;
            }

            StartMonitoring();
        }

        private void ShowWindow(object parameter) => OnRequestShowWindow?.Invoke(this, EventArgs.Empty);

        private void ExitApplication(object parameter)
        {
            if (MessageDialog.ShowConfirm("是否退出程序？"))
            {
                StopMonitoring();
                OnRequestExit?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Initialize()
        {
            if (BoundDevices.Count > 0)
                StartMonitoring();
            else
            {
                StatusText = "待设置";
                CurrentRssi = "请先添加设备";
                IsDeviceInRange = false;
            }
            RefreshSetupHint();
        }

        public void Cleanup() => StopMonitoring();

        private void InitializeUnlockManager()
        {
            try
            {
                _unlockManager = new UnlockManager
                {
                    rssiyuzhi = Settings.RssiThreshold,
                    hysteresisDb = Settings.HysteresisDb,
                    presenceTimeout = Settings.PresenceTimeout,
                    isautolock = Settings.AutoLock,
                    isautounlock = Settings.AutoUnlock,
                    manuallock = Settings.ManualLock,
                    manualunlock = Settings.ManualUnlock,
                    lockWhenSeen = Settings.LockWhenSeen,
                    lockDelay = Settings.LockDelay,
                    unlockDelay = Settings.UnlockDelay,
                    actionWarnSeconds = Settings.ActionWarnSeconds,
                    lockOnlyWhenIdle = Settings.LockOnlyWhenIdle,
                    idleLockSeconds = Settings.IdleLockSeconds,
                    requireAllDevices = Settings.RequireAllDevices,
                    useLocalUnlock = Settings.UseLocalUnlock
                };
                _unlockManager.SetBoundDevices(BoundDevices);
                _unlockManager.UpdategRssi = UpdateRssiDisplay;
                _unlockManager.UpdateDevicePresence = UpdateDevicePresence;
                _unlockManager.UnlockTestFinished = OnUnlockTestFinished;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"初始化解锁管理器失败: {ex.Message}");
            }
        }

        private void StartMonitoring()
        {
            _monitorVersion++;
            try
            {
                if (_unlockManager == null)
                    InitializeUnlockManager();
                SyncBoundDevicesToManager(false);
                _unlockManager?.Start();
                IsMonitoring = true;
                StatusText = "监控中";
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
            _monitorVersion++;
            try
            {
                _unlockManager?.Stop();
                IsMonitoring = false;
                StatusText = "已停止";
                IsConnected = false;
                CurrentRssi = "--";
            }
            catch (Exception ex)
            {
                LogHelper.WriteLine($"停止监控失败: {ex.Message}");
            }
        }

        private void SyncBoundDevicesToManager(bool restartIfNeeded)
        {
            if (_unlockManager == null) return;
            var oldType = _unlockManager.bletype;
            _unlockManager.SetBoundDevices(BoundDevices);
            if (!IsMonitoring) return;
            if (restartIfNeeded || _unlockManager.bletype != oldType)
            {
                _monitorVersion++;
                _unlockManager.Stop();
                _unlockManager.Start();
            }
        }

        private void UpdateRssiDisplay(string displayText, bool inRange)
        {
            var version = _monitorVersion;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (!IsMonitoring || version != _monitorVersion) return;
                CurrentRssi = displayText;
                IsDeviceInRange = inRange;
            }));
        }

        private void UpdateDevicePresence(string address, short rssi, bool inRange, string status)
        {
            var version = _monitorVersion;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                if (!IsMonitoring || version != _monitorVersion) return;
                var device = BoundDevices.FirstOrDefault(d =>
                    address.Equals(d.NormalizedAddress, StringComparison.OrdinalIgnoreCase));
                if (device == null) return;
                device.Rssi = rssi;
                device.IsInRange = inRange;
                device.StatusText = status;
            }));
        }

        private void ApplySettingsToManager()
        {
            if (_unlockManager == null) return;
            _unlockManager.ApplyRuntimeSettings(
                Settings.RssiThreshold,
                Settings.HysteresisDb,
                Settings.PresenceTimeout,
                Settings.LockDelay,
                Settings.UnlockDelay,
                Settings.ActionWarnSeconds,
                Settings.AutoLock,
                Settings.AutoUnlock,
                Settings.ManualLock,
                Settings.ManualUnlock,
                Settings.RequireAllDevices,
                Settings.UseLocalUnlock,
                Settings.LockOnlyWhenIdle,
                Settings.IdleLockSeconds,
                Settings.LockWhenSeen);
            SyncBoundDevicesToManager(false);
        }

        private void RefreshLocalUnlockStatus()
        {
            LocalUnlockStatus = LocalUnlock.StatusText();
            OnPropertyChanged(nameof(IsLocalUnlockReady));
            OnPropertyChanged(nameof(CanUninstallLocalUnlock));
            OnPropertyChanged(nameof(CanTestUnlock));
            OnPropertyChanged(nameof(InstallButtonText));
            (TestUnlockCommand as RelayCommand)?.RaiseCanExecuteChanged();
            RefreshSetupHint();
        }

        private void RefreshSetupHint()
        {
            OnPropertyChanged(nameof(SetupHint));
            OnPropertyChanged(nameof(HasSetupHint));
            OnPropertyChanged(nameof(HasMultipleDevices));
        }

        public event EventHandler<int> OnRequestSearchDevice;
        public event EventHandler OnRequestShowWindow;
        public event EventHandler OnRequestExit;
    }
}
