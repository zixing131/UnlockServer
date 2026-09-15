using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace UnlockServer.Views
{
    public static class ToastService
    {
        public static void Show(string message, int seconds = 4)
        {
            var app = Application.Current;
            if (app == null) return;

            void ShowCore()
            {
                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.WidthAndHeight,
                    ShowActivated = false
                };

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(28, 30, 38)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 86, 110)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(10),
                    MaxWidth = 360,
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 18,
                        ShadowDepth = 0,
                        Opacity = 0.45,
                        Color = Colors.Black
                    }
                };

                var text = new TextBlock
                {
                    Text = message ?? "",
                    Foreground = Brushes.White,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                };
                border.Child = text;
                window.Content = border;

                window.Loaded += (s, e) =>
                {
                    var work = SystemParameters.WorkArea;
                    window.Left = work.Right - window.ActualWidth - 18;
                    window.Top = work.Bottom - window.ActualHeight - 18;
                };

                window.Show();

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds > 0 ? seconds : 4) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    window.Close();
                };
                timer.Start();
            }

            if (app.Dispatcher.CheckAccess())
                ShowCore();
            else
                app.Dispatcher.Invoke(ShowCore);
        }
    }
}
