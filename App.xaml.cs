using System;
using System.Threading;
using System.Windows;

namespace EmojiManager
{
    public partial class App : Application
    {
        private Mutex? _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 检查是否已有实例在运行
            _singleInstanceMutex = new Mutex(true, "EmojiManager_SingleInstance", out var isNewInstance);

            if (!isNewInstance)
            {
                MessageBox.Show("表情管理器已经在运行中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // 设置未处理异常处理
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 释放Mutex
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            LogError(ex);
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogError(e.Exception);
            e.Handled = true;
        }

        private static void LogError(Exception? ex)
        {
            MessageBox.Show($"发生错误：{ex?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}