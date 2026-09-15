using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using UnlockServer.ViewModels;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace UnlockServer.Views
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private NotifyIcon _notifyIcon;
        private bool _isExiting;

        public MainWindow()
        {
            InitializeComponent();

            WanClient.reloadConfig();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // 订阅 ViewModel 事件
            _viewModel.OnRequestSearchDevice += ViewModel_OnRequestSearchDevice;
            _viewModel.OnRequestShowWindow += ViewModel_OnRequestShowWindow;
            _viewModel.OnRequestExit += ViewModel_OnRequestExit;

            // 初始化系统托盘
            InitializeNotifyIcon();

            // 加载密码到 PasswordBox
            PasswordBox.Password = _viewModel.Settings.Password;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Initialize();

            // 如果是隐藏启动，直接最小化到托盘
            if (App.IsHideRun)
            {
                Hide();
            }
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                Text = "蓝牙解锁工具",
                Visible = true
            };

            // 双击托盘图标显示窗口
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            // 右键菜单
            var contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("显示主窗口");
            showItem.Click += (s, e) => ShowMainWindow();
            contextMenu.Items.Add(showItem);

            var lockItem = new ToolStripMenuItem("立即锁屏");
            lockItem.Click += (s, e) => _viewModel.LockScreenCommand.Execute(null);
            contextMenu.Items.Add(lockItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var hideItem = new ToolStripMenuItem("隐藏到托盘");
            hideItem.Click += (s, e) => Hide();
            contextMenu.Items.Add(hideItem);

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (s, e) =>
            {
                if (MessageDialog.ShowConfirm("是否退出程序？"))
                {
                    ExitApplication();
                }
            };
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_isExiting)
            {
                // 真正退出
                _viewModel.Cleanup();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            else
            {
                // 最小化到托盘
                e.Cancel = true;
                Hide();
            }
        }

        private void ExitApplication()
        {
            _isExiting = true;
            _viewModel.Cleanup();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        #region 无边框窗口控制

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击标题栏不做任何事（无边框窗口不支持最大化）
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region ViewModel 事件处理

        private void ViewModel_OnRequestSearchDevice(object sender, int bluetoothType)
        {
            var deviceListWindow = new DeviceListWindow(bluetoothType, _viewModel.Settings.DeviceAddress)
            {
                Owner = this
            };

            if (deviceListWindow.ShowDialog() == true)
            {
                _viewModel.AddBoundDevice(deviceListWindow.SelectedAddress, deviceListWindow.SelectedBluetoothType);
            }
        }

        private void ViewModel_OnRequestShowWindow(object sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void ViewModel_OnRequestExit(object sender, EventArgs e)
        {
            ExitApplication();
        }

        #endregion

        #region UI 事件处理

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_viewModel?.Settings != null)
            {
                _viewModel.Settings.Password = PasswordBox.Password;
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://www.52pojie.cn/thread-1678522-1-1.html")
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }

        #endregion
    }
}
