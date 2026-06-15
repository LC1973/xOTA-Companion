using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using xOTACompanion.Models;

namespace xOTACompanion.Services
{
    /// <summary>
    /// Read-only access to the GreenLogger/QSOLogger SQLite database.
    /// Supplements xOTA Companion config with operators, radios, and tokens
    /// already configured in GreenLogger, so the user only needs to configure once.
    /// </summary>
    public static class GreenLoggerDbService
    {
        /// <summary>
        /// Set to true via the --no-gl command-line switch to simulate GreenLogger being absent.
        /// </summary>
        public static bool NoGreenLogger { get; set; } = false;

        private static readonly string DefaultDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QSOLogger", "qso.db");

        private static readonly string DbConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "QSOLogger", "dbconfig.json");

        /// <summary>Returns the SQLite DB path, or null if GreenLogger is not installed.</summary>
        public static string? GetDbPath()
        {
            if (NoGreenLogger) return null;
            try
            {
                if (File.Exists(DbConfigPath))
                {
                    var json = File.ReadAllText(DbConfigPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("DatabasePath", out var pathProp))
                    {
                        var configured = pathProp.GetString();
                        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                            return configured;
                    }
                }
                if (File.Exists(DefaultDbPath))
                    return DefaultDbPath;
            }
            catch { /* GL not installed or config malformed */ }
            return null;
        }

        public static bool IsAvailable() => GetDbPath() != null;

        /// <summary>
        /// Supplements an already-loaded AppConfig with data from the GreenLogger DB.
        /// Existing SOTA API keys are preserved when merging operators.
        /// Called synchronously from ConfigService.Load() — SQLite reads are fast (&lt;10 ms).
        /// </summary>
        public static void SupplementConfig(AppConfig cfg)
        {
            var dbPath = GetDbPath();
            if (dbPath == null) return;

            try
            {
                using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                connection.Open();

                // --- Operators ---
                var glOperators = ReadOperators(connection);
                if (glOperators.Count > 0)
                {
                    // Preserve any SOTA keys the user already entered in xOTA
                    var sotaKeys = cfg.Operators
                        .Where(o => !string.IsNullOrEmpty(o.SotaApiKey))
                        .ToDictionary(o => o.Callsign, o => o.SotaApiKey!);

                    cfg.Operators = glOperators;
                    foreach (var op in cfg.Operators)
                    {
                        if (sotaKeys.TryGetValue(op.Callsign, out var key))
                            op.SotaApiKey = key;
                    }
                }

                // --- Radios ---
                bool glHasCivAddr = TableHasColumn(connection, "Radios", "CIVAddress");
                var glRadios = ReadRadios(connection, glHasCivAddr);
                if (glRadios.Count > 0)
                {
                    // When GL's schema pre-dates CI-V support, carry forward any CI-V address
                    // already saved in xOTA's own config for the matching radio.
                    if (!glHasCivAddr)
                    {
                        var civMap = cfg.Radios
                            .Where(r => r.ControlType == "CIV")
                            .ToDictionary(r => r.RadioId, r => r.CIVAddress);
                        foreach (var r in glRadios.Where(r => r.ControlType == "CIV"))
                        {
                            if (civMap.TryGetValue(r.RadioId, out int addr))
                                r.CIVAddress = addr;
                        }
                    }
                    cfg.Radios = glRadios;
                }

                // --- Active radio ---
                // Only use GreenLogger's active radio as a default when xOTA has no
                // saved preference (ActiveRadioId == 0). Once the user selects a radio
                // in the startup window it is persisted and must not be overridden here.
                if (cfg.ActiveRadioId == 0)
                {
                    var activeId = ReadStringConfig(connection, "application", "activeradioid");
                    if (int.TryParse(activeId, out int radioId) &&
                        cfg.Radios.Any(r => r.RadioId == radioId))
                    {
                        cfg.ActiveRadioId = radioId;
                    }
                }

                // --- Mapbox token (only if xOTA config doesn't already have one) ---
                if (string.IsNullOrEmpty(cfg.MapboxAccessToken))
                {
                    var token = ReadStringConfig(connection, "map", "mapboxaccesstoken");
                    if (!string.IsNullOrEmpty(token))
                        cfg.MapboxAccessToken = token;
                }

                cfg.LoadedFromGreenLogger = true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.General,
                    $"GreenLoggerDb: could not supplement config — {ex.Message}");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static List<OperatorProfile> ReadOperators(SqliteConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT Value FROM Configuration WHERE Category='qrz' AND Key='operatorlogs' LIMIT 1";

            var raw = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(raw)) return [];

            var glList = JsonSerializer.Deserialize<List<GlOperatorLogConfig>>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (glList == null) return [];

            return glList
                .Where(o => o.IsEnabled && !string.IsNullOrWhiteSpace(o.Callsign))
                .Select(o => new OperatorProfile
                {
                    Callsign = o.Callsign.Trim().ToUpperInvariant(),
                    Name = o.Name ?? string.Empty,
                    Locator = (o.Locator ?? string.Empty).Trim().ToUpperInvariant(),
                    IsDefault = false   // resolved below via ActiveOperatorCallsign
                })
                .ToList();
        }

        private static bool TableHasColumn(SqliteConnection connection, string table, string column)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static List<RadioConfig> ReadRadios(SqliteConnection connection, bool includeCivAddress)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT Id, FriendlyName, ControlType, IsDefault, " +
                "       TCIHost, TCIPort, CATPortName, CATBaudRate" +
                (includeCivAddress ? ", CIVAddress" : "") +
                " FROM Radios ORDER BY IsDefault DESC, FriendlyName";

            var radios = new List<RadioConfig>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                radios.Add(new RadioConfig
                {
                    RadioId      = reader.GetInt32(0),
                    FriendlyName = reader.GetString(1),
                    ControlType  = reader.GetString(2),
                    IsDefault    = reader.GetInt32(3) != 0,
                    TCIHost      = reader.IsDBNull(4) ? "127.0.0.1" : reader.GetString(4),
                    TCIPort      = reader.IsDBNull(5) ? 40001       : reader.GetInt32(5),
                    CATPortName  = reader.IsDBNull(6) ? "COM1"      : reader.GetString(6),
                    CATBaudRate  = reader.IsDBNull(7) ? 38400       : reader.GetInt32(7),
                    // 0 = unset — caller carries over xOTA's saved address when GL's
                    // schema pre-dates the column. RadioConfig.IsValid catches stray 0s.
                    CIVAddress   = includeCivAddress && !reader.IsDBNull(8) ? reader.GetInt32(8) : 0,
                });
            }
            return radios;
        }

        private static string? ReadStringConfig(
            SqliteConnection connection, string category, string key)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT Value FROM Configuration WHERE Category=@cat AND Key=@key LIMIT 1";
            cmd.Parameters.AddWithValue("@cat", category);
            cmd.Parameters.AddWithValue("@key", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    // Minimal model matching GreenLogger's operatorlogs JSON structure
    internal sealed class GlOperatorLogConfig
    {
        public string Callsign  { get; set; } = string.Empty;
        public string Name      { get; set; } = string.Empty;
        public bool   IsEnabled { get; set; } = true;
        public string Locator   { get; set; } = string.Empty;
    }
}
