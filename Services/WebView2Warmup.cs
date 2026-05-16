using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Runtime.Versioning;

namespace xOTACompanion.Services
{
    /// <summary>
    /// Pre-warms the shared WebView2 environment so map windows open faster.
    /// Ported from GreenLogger_New.
    /// </summary>
    [SupportedOSPlatform("windows10.0.17763.0")]
    public static class WebView2Warmup
    {
        public static readonly string UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "xOTA Companion", "WebView2");

        private static CoreWebView2Environment? _env;
        private static Task<CoreWebView2Environment?>? _warmupTask;
        private static readonly object _lock = new();

        public static void StartWarmup()
        {
            lock (_lock)
            {
                if (_warmupTask == null)
                    _warmupTask = Task.Run(WarmupAsync);
            }
        }

        private static async Task<CoreWebView2Environment?> WarmupAsync()
        {
            try
            {
                Directory.CreateDirectory(UserDataFolder);
                _env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder);
                Logger.Instance.Log(LogCategory.Map, "WebView2Warmup: environment ready");
                return _env;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.Map, $"WebView2Warmup: {ex.Message}");
                return null;
            }
        }

        public static async Task<CoreWebView2Environment?> GetEnvironmentAsync()
        {
            if (_env != null) return _env;
            Task<CoreWebView2Environment?>? t;
            lock (_lock) { t = _warmupTask; }
            return t != null ? await t : null;
        }
    }
}
