using System.Windows;
using System.Windows.Media;

namespace UnlockServer.Views
{
    /// <summary>
    /// 自定义消息对话框
    /// </summary>
    public partial class MessageDialog : Window
    {
        public enum DialogType
        {
            Info,
            Success,
            Warning,
            Error,
            Question
        }

        public enum DialogButtons
        {
            Ok,
            OkCancel,
            YesNo
        }

        public bool Result { get; private set; }

        public MessageDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 显示消息对话框
        /// </summary>
        public static bool Show(string message, string title = "提示", DialogType type = DialogType.Info, DialogButtons buttons = DialogButtons.Ok, Window owner = null)
        {
            var dialog = new MessageDialog();
            dialog.Owner = owner ?? Application.Current.MainWindow;
            dialog.SetContent(message, title, type, buttons);
            dialog.ShowDialog();
            return dialog.Result;
        }

        /// <summary>
        /// 显示信息提示
        /// </summary>
        public static void ShowInfo(string message, string title = "提示", Window owner = null)
        {
            Show(message, title, DialogType.Info, DialogButtons.Ok, owner);
        }

        /// <summary>
        /// 显示成功提示
        /// </summary>
        public static void ShowSuccess(string message, string title = "成功", Window owner = null)
        {
            Show(message, title, DialogType.Success, DialogButtons.Ok, owner);
        }

        /// <summary>
        /// 显示警告提示
        /// </summary>
        public static void ShowWarning(string message, string title = "警告", Window owner = null)
        {
            Show(message, title, DialogType.Warning, DialogButtons.Ok, owner);
        }

        /// <summary>
        /// 显示错误提示
        /// </summary>
        public static void ShowError(string message, string title = "错误", Window owner = null)
        {
            Show(message, title, DialogType.Error, DialogButtons.Ok, owner);
        }

        /// <summary>
        /// 显示确认对话框
        /// </summary>
        public static bool ShowConfirm(string message, string title = "确认", Window owner = null)
        {
            return Show(message, title, DialogType.Question, DialogButtons.YesNo, owner);
        }

        private void SetContent(string message, string title, DialogType type, DialogButtons buttons)
        {
            TitleText.Text = title;
            MessageText.Text = message;

            // 设置图标和颜色
            switch (type)
            {
                case DialogType.Info:
                    IconText.Text = "ℹ️";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                    break;
                case DialogType.Success:
                    IconText.Text = "✓";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    break;
                case DialogType.Warning:
                    IconText.Text = "⚠";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    break;
                case DialogType.Error:
                    IconText.Text = "✕";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    break;
                case DialogType.Question:
                    IconText.Text = "?";
                    IconBorder.Background = new SolidColorBrush(Color.FromRgb(139, 92, 246));
                    break;
            }

            // 设置按钮
            switch (buttons)
            {
                case DialogButtons.Ok:
                    CancelButton.Visibility = Visibility.Collapsed;
                    OkButton.Content = "确定";
                    break;
                case DialogButtons.OkCancel:
                    CancelButton.Visibility = Visibility.Visible;
                    CancelButton.Content = "取消";
                    OkButton.Content = "确定";
                    break;
                case DialogButtons.YesNo:
                    CancelButton.Visibility = Visibility.Visible;
                    CancelButton.Content = "否";
                    OkButton.Content = "是";
                    break;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}

