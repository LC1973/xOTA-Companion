using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using xOTACompanion.Models;

namespace xOTACompanion.Services
{
    /// <summary>
    /// Fetches active POTA spots and posts self-spots via api.pota.app
    /// </summary>
    public class PotaService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private const string SpotListUrl = "https://api.pota.app/spot/activator";
        private const string SelfSpotUrl = "https://api.pota.app/spot/activator";

        /// <summary>Fetch all active POTA spots.</summary>
        public async Task<List<SpotModel>> FetchSpotsAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(SpotListUrl);
                var raw = JsonSerializer.Deserialize<List<PotaSpotRaw>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (raw == null) return new();

                return raw
                    .Where(r => !string.IsNullOrWhiteSpace(r.Activator))
                    .Select(MapSpot)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.POTA, $"PotaService: FetchSpots – {ex.Message}");
                return new();
            }
        }

        /// <summary>Post a self-spot to POTA.</summary>
        public async Task<(bool Ok, string Message)> PostSelfSpotAsync(
            string callsign, string reference, double freqKhz, string mode, string comment = "")
        {
            try
            {
                var payload = new
                {
                    activator = callsign.Trim().ToUpperInvariant(),
                    spotter   = callsign.Trim().ToUpperInvariant(),
                    frequency = freqKhz.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                    mode      = mode.Trim().ToUpperInvariant(),
                    reference = reference.Trim().ToUpperInvariant(),
                    source    = "Web",
                    comments  = comment.Trim()
                };

                var json    = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp    = await _http.PostAsync(SelfSpotUrl, content);
                var body    = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                    return (true, $"POTA spot posted for {reference} on {freqKhz:F0} kHz {mode}");

                Logger.Instance.Log(LogCategory.POTA, $"PotaService: POST {(int)resp.StatusCode} – {body}");
                return (false, $"POTA spot failed ({(int)resp.StatusCode}): {body}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.POTA, $"PotaService: PostSelfSpot – {ex.Message}");
                return (false, $"POTA spot error: {ex.Message}");
            }
        }

        private static SpotModel MapSpot(PotaSpotRaw r)
        {
            double.TryParse(r.Frequency, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double freqKhz);
            double freqMhz = freqKhz / 1000.0;

            double? lat = r.Latitude;
            double? lon = r.Longitude;

            // Build a sensible grid: prefer grid6, fall back to grid4
            string grid = !string.IsNullOrWhiteSpace(r.Grid6) ? r.Grid6
                        : !string.IsNullOrWhiteSpace(r.Grid4) ? r.Grid4
                        : string.Empty;

            // Fallback: derive lat/lon from grid when API provides no coordinates
            if ((!lat.HasValue || !lon.HasValue) && !string.IsNullOrWhiteSpace(grid) && grid.Length >= 4)
            {
                var (gLat, gLon) = MaidenheadService.LocatorToCoordinates(grid);
                lat = gLat;
                lon = gLon;
            }

            DateTime.TryParse(r.SpotTime, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTime spotTime);

            return new SpotModel
            {
                Source       = SpotSource.POTA,
                Activator    = r.Activator ?? string.Empty,
                Name         = r.ParkName ?? r.Name ?? string.Empty,
                Reference    = r.Reference ?? string.Empty,
                LocationDesc = r.LocationDesc ?? string.Empty,
                FrequencyMhz = freqMhz,
                Mode         = r.Mode ?? string.Empty,
                Grid         = grid,
                Latitude     = lat,
                Longitude    = lon,
                SpottedUtc   = spotTime == default ? DateTime.UtcNow : spotTime,
                Spotter      = r.Spotter ?? string.Empty,
                Comments     = r.Comments ?? string.Empty,
                SpotCount    = r.Count
            };
        }

        // ---- JSON DTO ----
        private class PotaSpotRaw
        {
            [JsonPropertyName("activator")]    public string? Activator    { get; set; }
            [JsonPropertyName("frequency")]    public string? Frequency    { get; set; }
            [JsonPropertyName("mode")]         public string? Mode         { get; set; }
            [JsonPropertyName("reference")]    public string? Reference    { get; set; }
            [JsonPropertyName("park_name")]    public string? ParkName     { get; set; }
            [JsonPropertyName("name")]         public string? Name         { get; set; }
            [JsonPropertyName("spotTime")]    public string? SpotTime     { get; set; }
            [JsonPropertyName("expire")]       public int     Expire       { get; set; }
            [JsonPropertyName("comments")]     public string? Comments     { get; set; }
            [JsonPropertyName("grid4")]        public string? Grid4        { get; set; }
            [JsonPropertyName("grid6")]        public string? Grid6        { get; set; }
            [JsonPropertyName("latitude")]     public double? Latitude     { get; set; }
            [JsonPropertyName("longitude")]    public double? Longitude    { get; set; }
            [JsonPropertyName("count")]        public int     Count        { get; set; }
            [JsonPropertyName("spotter")]      public string? Spotter      { get; set; }
            [JsonPropertyName("locationDesc")] public string? LocationDesc { get; set; }
        }
    }
}
