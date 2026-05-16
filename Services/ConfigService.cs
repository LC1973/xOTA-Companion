using System.IO;
using System.Text.Json;
using xOTACompanion.Models;

namespace xOTACompanion.Services
{
    /// <summary>
    /// Loads and saves AppConfig as JSON from %APPDATA%\xOTA Companion\config.json
    /// </summary>
    public static class ConfigService
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "xOTA Companion");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static AppConfig Load()
        {
            AppConfig cfg;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    cfg = JsonSerializer.Deserialize<AppConfig>(json, _opts) ?? new AppConfig();
                }
                else
                {
                    cfg = new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.General, $"ConfigService: Load error – {ex.Message}");
                cfg = new AppConfig();
            }

            // Supplement from the GreenLogger/QSOLogger SQLite DB when available.
            // This lets xOTA Companion share operator and radio config with GreenLogger
            // without any duplicate setup.
            GreenLoggerDbService.SupplementConfig(cfg);

            return cfg;
        }

        public static void Save(AppConfig config)
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                var json = JsonSerializer.Serialize(config, _opts);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.General, $"ConfigService: Save error – {ex.Message}");
            }
        }
    }
}
