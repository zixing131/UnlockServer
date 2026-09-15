using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
                var window = CreateWindow();
                var border = CreateBorder();
                border.Child = new TextBlock
                {
                    Text = message ?? "",
                    Foreground = Brushes.White,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                };
                window.Content = border;
                PlaceBottomRight(window);
                window.Show();

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds > 0 ? seconds : 4) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    try { window.Close(); } catch { }
                };
                timer.Start();
            }

            if (app.Dispatcher.CheckAccess())
                ShowCore();
            else
                app.Dispatcher.Invoke(ShowCore);
        }

        public static ActionToast ShowAction(string title, string hint, int seconds, Action onCancel, Action onTimeout)
        {
            var app = Application.Current;
            if (app == null) return ActionToast.Empty;

            ActionToast toast = null;
            void ShowCore() => toast = ActionToast.ShowCore(title, hint, seconds, onCancel, onTimeout);

            if (app.Dispatcher.CheckAccess())
                ShowCore();
            else
                app.Dispatcher.Invoke(ShowCore);
            return toast ?? ActionToast.Empty;
        }

        internal static Window CreateWindow()
        {
            return new Window
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
        }

        internal static Border CreateBorder()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(76, 141, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(10),
                MaxWidth = 380,
                Cursor = Cursors.Hand,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.45,
                    Color = Colors.Black
                }
            };
        }

        internal static void PlaceBottomRight(Window window)
        {
            window.Loaded += (s, e) =>
            {
                var work = SystemParameters.WorkArea;
                window.Left = work.Right - window.ActualWidth - 18;
                window.Top = work.Bottom - window.ActualHeight - 18;
            };
        }
    }

    public sealed class ActionToast : IDisposable
    {
        public static readonly ActionToast Empty = new ActionToast();

        private Window _window;
        private DispatcherTimer _timer;
        private TextBlock _title;
        private bool _closed;
        private int _remain;
        private string _titleText;
        private Action _onCancel;
        private Action _onTimeout;

        private ActionToast() { }

        internal static ActionToast ShowCore(string title, string hint, int seconds, Action onCancel, Action onTimeout)
        {
            var toast = new ActionToast
            {
                _remain = seconds > 0 ? seconds : 1,
                _titleText = title ?? "",
                _onCancel = onCancel,
                _onTimeout = onTimeout
            };

            toast._window = ToastService.CreateWindow();
            var border = ToastService.CreateBorder();

            var stack = new StackPanel();
            toast._title = new TextBlock
            {
                Text = toast.BuildTitle(),
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(toast._title);
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(hint) ? "点击这条提示可取消" : hint,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 186, 200)),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            border.Child = stack;
            toast._window.Content = border;
            toast._window.MouseLeftButtonUp += (s, e) => toast.Cancel();
            ToastService.PlaceBottomRight(toast._window);
            toast._window.Show();

            toast._timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            toast._timer.Tick += (s, e) => toast.Tick();
            toast._timer.Start();
            return toast;
        }

        private string BuildTitle() => $"{_titleText}  {_remain} 秒";

        private void Tick()
        {
            if (_closed) return;
            _remain--;
            if (_remain <= 0)
            {
                var done = _onTimeout;
                Close();
                done?.Invoke();
                return;
            }
            if (_title != null)
                _title.Text = BuildTitle();
        }

        public void Cancel()
        {
            if (_closed) return;
            var cancel = _onCancel;
            Close();
            cancel?.Invoke();
        }

        public void Dismiss()
        {
            if (_closed) return;
            Close();
        }

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            _onCancel = null;
            _onTimeout = null;
            try { _timer?.Stop(); } catch { }
            try { _window?.Close(); } catch { }
            _timer = null;
            _window = null;
        }

        public void Dispose() => Dismiss();
    }
}
