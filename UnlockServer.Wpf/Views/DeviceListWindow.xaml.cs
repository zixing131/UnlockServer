using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using UnlockServer.ViewModels;

namespace UnlockServer.Views
{
    /// <summary>
    /// DeviceListWindow.xaml 的交互逻辑
    /// </summary>
    public partial class DeviceListWindow : Window
    {
        private readonly DeviceListViewModel _viewModel;

        public string SelectedAddress { get; private set; }
        public int SelectedBluetoothType { get; private set; }

        public DeviceListWindow(int bluetoothType = 1, string initialAddress = "")
        {
            InitializeComponent();

            // 从地址中提取纯 MAC 地址
            var pureAddress = ExtractAddress(initialAddress);

            _viewModel = new DeviceListViewModel(bluetoothType, pureAddress);
            DataContext = _viewModel;

            _viewModel.OnDeviceSelected += ViewModel_OnDeviceSelected;

            Loaded += DeviceListWindow_Loaded;
        }

        private void DeviceListWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.StartScanning();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _viewModel.OnDeviceSelected -= ViewModel_OnDeviceSelected;
            _viewModel.Dispose();
        }

        private void ViewModel_OnDeviceSelected(object sender, DeviceSelectedEventArgs e)
        {
            SelectedAddress = e.Address;
            SelectedBluetoothType = e.BluetoothType;
            DialogResult = true;
            Close();
        }

        #region 无边框窗口控制

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 从复合地址字符串中提取纯 MAC 地址
        /// </summary>
        private static string ExtractAddress(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // 尝试从 "Name[AA:BB:CC:DD:EE:FF]" 格式中提取
            var startIndex = input.LastIndexOf('[');
            var endIndex = input.LastIndexOf(']');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                return input.Substring(startIndex + 1, endIndex - startIndex - 1);
            }

            return input;
        }
    }
}
