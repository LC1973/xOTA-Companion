using System.IO;
using System.Net.Http;

namespace xOTACompanion.Services
{
    /// <summary>
    /// Downloads and caches the SOTA summits list CSV, providing lat/lon lookups by summit code.
    /// Cache is stored in %APPDATA%\xOTA Companion\summitslist.csv and refreshed every 7 days.
    /// </summary>
    public class SotaSummitService
    {
        private static readonly Lazy<SotaSummitService> _instance = new(() => new());
        public static SotaSummitService Instance => _instance.Value;

        private const string CsvUrl   = "https://storage.sota.org.uk/summitslist.csv";
        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "xOTA Companion", "summitslist.csv");
        private const int CacheDays = 7;

        public record SummitInfo(double Latitude, double Longitude, int Points, int AltM, string Name);

        private Dictionary<string, SummitInfo> _summits = new(StringComparer.OrdinalIgnoreCase);
        private bool _loaded;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private SotaSummitService() { }

        /// <summary>
        /// Ensures the summit list is loaded (downloading/refreshing if necessary).
        /// Safe to call concurrently; only one download will occur.
        /// </summary>
        public async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            await _lock.WaitAsync();
            try
            {
                if (_loaded) return;
                await LoadAsync();
                _loaded = true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Look up a summit by its code (e.g. "G/SP-001"). Returns false if not found.</summary>
        public bool TryGetSummit(string summitCode, out SummitInfo? info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(summitCode)) return false;
            return _summits.TryGetValue(summitCode.Trim(), out info);
        }

        private async Task LoadAsync()
        {
            bool needsDownload = true;
            if (File.Exists(CachePath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath);
                if (age.TotalDays < CacheDays)
                    needsDownload = false;
            }

            if (needsDownload)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("xOTACompanion/1.0");
                    var csv = await http.GetStringAsync(CsvUrl);
                    await File.WriteAllTextAsync(CachePath, csv, System.Text.Encoding.UTF8);
                    Logger.Instance.Log(LogCategory.SOTA, "SotaSummitService: summit list downloaded.");
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log(LogCategory.SOTA, $"SotaSummitService: download failed – {ex.Message}");
                    if (!File.Exists(CachePath)) return; // No cache to fall back on
                }
            }

            try
            {
                var csv = await File.ReadAllTextAsync(CachePath, System.Text.Encoding.UTF8);
                ParseCsv(csv);
                Logger.Instance.Log(LogCategory.SOTA, $"SotaSummitService: loaded {_summits.Count} summits.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.SOTA, $"SotaSummitService: parse failed – {ex.Message}");
            }
        }

        private void ParseCsv(string csv)
        {
            // CSV columns (0-based):
            // 0=SummitCode, 1=AssociationName, 2=RegionName, 3=SummitName,
            // 4=AltM, 5=AltFt, 6=GridRef1, 7=GridRef2,
            // 8=Longitude, 9=Latitude, 10=Points, ...
            var dict = new Dictionary<string, SummitInfo>(16000, StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in csv.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.StartsWith("SOTA", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("SummitCode", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(line))
                    continue;

                var cols = SplitCsvLine(line);
                if (cols.Length < 11) continue;

                var code = cols[0].Trim();
                if (string.IsNullOrEmpty(code)) continue;

                if (!double.TryParse(cols[9].Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double lat)) continue;
                if (!double.TryParse(cols[8].Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double lon)) continue;

                int.TryParse(cols[10].Trim(), out int points);
                int.TryParse(cols[4].Trim(),  out int altM);
                var name = cols[3].Trim().Trim('"');

                dict[code] = new SummitInfo(lat, lon, points, altM, name);
            }

            _summits = dict;
        }

        /// <summary>Minimal RFC-4180 CSV line splitter (handles double-quoted fields with commas).</summary>
        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            int i = 0;
            while (i <= line.Length)
            {
                if (i == line.Length)
                {
                    fields.Add(string.Empty);
                    break;
                }

                if (line[i] == '"')
                {
                    // Quoted field
                    i++; // skip opening quote
                    var sb = new System.Text.StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            i++;
                            if (i < line.Length && line[i] == '"') { sb.Append('"'); i++; } // escaped quote
                            else break;
                        }
                        else { sb.Append(line[i]); i++; }
                    }
                    fields.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++; // skip comma
                }
                else
                {
                    // Unquoted field
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    fields.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++; // skip comma
                }
            }
            return fields.ToArray();
        }
    }
}
