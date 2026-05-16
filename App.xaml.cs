using System;
using System.Windows;
using xOTACompanion.Services;

namespace xOTACompanion
{
    public partial class App : Application
    {
        private void App_Startup(object sender, StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, ex) =>
            {
                var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xota_startup_error.txt");
                System.IO.File.WriteAllText(logPath,
                    $"DispatcherUnhandledException\r\n{ex.Exception.GetType().FullName}: {ex.Exception.Message}\r\n\r\n{ex.Exception.StackTrace}\r\n\r\nInner: {ex.Exception.InnerException}");
                MessageBox.Show($"Unexpected error:\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\nFull details saved to:\n{logPath}",
                    "xOTA Companion", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // With Startup= (not StartupUri=), WPF shuts down when the startup
            // dialog closes because it's the only open window. Switch to explicit
            // shutdown so we control the lifetime ourselves.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Allow testing the no-GreenLogger fallback mode without uninstalling GL.
            if (e.Args.Contains("--no-gl", StringComparer.OrdinalIgnoreCase))
                GreenLoggerDbService.NoGreenLogger = true;

            try
            {
                // Pre-warm WebView2 for faster map window first open
                WebView2Warmup.StartWarmup();

                var startup = new StartupWindow();
                startup.ShowDialog();

                if (startup.Launched)
                {
                    var main = new MainWindow(startup.SkippedRadio);
                    main.Show();
                }
                else
                {
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xota_startup_error.txt");
                System.IO.File.WriteAllText(logPath,
                    $"{ex.GetType().FullName}: {ex.Message}\r\n\r\n" +
                    $"Stack trace:\r\n{ex.StackTrace}\r\n\r\n" +
                    $"Inner: {ex.InnerException}");
                MessageBox.Show(
                    $"Startup error:\n\n{ex.GetType().Name}: {ex.Message}\n\nInner: {ex.InnerException?.Message}\n\nFull stack trace saved to:\n{logPath}",
                    "xOTA Companion – Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
}
