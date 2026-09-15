using System;
using System.Threading;
using System.Windows;

namespace UnlockServer
{
    public partial class App : Application
    {
        private static Mutex _mutex;
        public static bool IsHideRun { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], LocalUnlock.InstallArg, StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(LocalUnlock.RunElevatedInstall());
                return;
            }

            if (e.Args.Length > 0 &&
                string.Equals(e.Args[0], LocalUnlock.UninstallArg, StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(LocalUnlock.RunElevatedUninstall());
                return;
            }

            const string mutexName = "UnlockServer_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("程序已经在运行中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }

            if (e.Args.Length > 0 && e.Args[0].ToLower() == "hide")
            {
                IsHideRun = true;
            }

            LocalUnlock.EnsureInstalled();

            // 全局异常处理
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogHelper.WriteLine($"UI线程异常: {e.Exception.Message}\n{e.Exception.StackTrace}");
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogHelper.WriteLine($"非UI线程异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}

