using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using xOTACompanion.Models;
using xOTACompanion.Services;

namespace xOTACompanion.Services
{
    /// <summary>
    /// Fetches active SOTA spots and posts self-spots via api2.sota.org.uk
    /// </summary>
    public class SotaService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private const string SpotListUrl = "https://api2.sota.org.uk/api/spots/50/all";
        private const string SelfSpotUrl = "https://api2.sota.org.uk/api/spots";

        /// <summary>Fetch active SOTA spots.</summary>
        public async Task<List<SpotModel>> FetchSpotsAsync()
        {
            try
            {
                // Ensure summit list is cached so we can resolve coordinates
                await SotaSummitService.Instance.EnsureLoadedAsync();

                var json = await _http.GetStringAsync(SpotListUrl);
                var raw  = JsonSerializer.Deserialize<List<SotaSpotRaw>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (raw == null) return new();

                return raw
                    .Where(r => !string.IsNullOrWhiteSpace(r.ActivatorCallsign))
                    .Select(MapSpot)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.SOTA, $"SotaService: FetchSpots – {ex.Message}");
                return new();
            }
        }

        /// <summary>
        /// Post a self-spot to SOTAwatch.
        /// Requires the activator's SOTAwatch API key (Bearer token).
        /// </summary>
        public async Task<(bool Ok, string Message)> PostSelfSpotAsync(
            string callsign, string summitCode, double freqMhz, string mode,
            string apiKey, string comment = "")
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return (false, "SOTAwatch API key not configured. Add it in Settings → Operators.");

            try
            {
                var payload = new
                {
                    activatorCallsign = callsign.Trim().ToUpperInvariant(),
                    summitCode        = summitCode.Trim().ToUpperInvariant(),
                    frequency         = freqMhz.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    mode              = mode.Trim().ToUpperInvariant(),
                    callsign          = callsign.Trim().ToUpperInvariant(),
                    comments          = comment.Trim()
                };

                var json    = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var req = new HttpRequestMessage(HttpMethod.Post, SelfSpotUrl);
                req.Headers.Add("Authorization", $"Bearer {apiKey}");
                req.Content = content;

                var resp = await _http.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                    return (true, $"SOTA spot posted for {summitCode} on {freqMhz:F3} MHz {mode}");

                Logger.Instance.Log(LogCategory.SOTA, $"SotaService: POST {(int)resp.StatusCode} – {body}");
                return (false, $"SOTA spot failed ({(int)resp.StatusCode}): {body}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.SOTA, $"SotaService: PostSelfSpot – {ex.Message}");
                return (false, $"SOTA spot error: {ex.Message}");
            }
        }

        private static SpotModel MapSpot(SotaSpotRaw r)
        {
            double.TryParse(r.Frequency, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double freqMhz);

            DateTime.TryParse(r.TimeStamp,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTime spottedUtc);

            // Prefer summit-list coordinates; fall back to Maidenhead if present
            string grid = r.Maidenhead ?? string.Empty;
            double? lat = null, lon = null;
            string summitName = r.SummitDetails ?? string.Empty;

            // Build full summit reference e.g. "CT/TM-031" from associationCode + summitCode
            string fullSummitCode = (!string.IsNullOrWhiteSpace(r.AssociationCode) && !string.IsNullOrWhiteSpace(r.SummitCode))
                ? $"{r.AssociationCode.Trim()}/{r.SummitCode.Trim()}"
                : r.SummitCode?.Trim() ?? string.Empty;

            if (SotaSummitService.Instance.TryGetSummit(fullSummitCode, out var info) && info != null)
            {
                lat       = info.Latitude;
                lon       = info.Longitude;
                summitName = string.IsNullOrWhiteSpace(summitName) ? info.Name : summitName;
            }
            else if (!string.IsNullOrWhiteSpace(grid) && grid.Length >= 4)
            {
                var (gLat, gLon) = MaidenheadService.LocatorToCoordinates(grid);
                lat = gLat;
                lon = gLon;
            }

            return new SpotModel
            {
                Source       = SpotSource.SOTA,
                Activator    = r.ActivatorCallsign ?? string.Empty,
                Name         = summitName,
                Reference    = fullSummitCode,
                LocationDesc = r.AssociationCode ?? string.Empty,
                FrequencyMhz = freqMhz,
                Mode         = r.Mode ?? string.Empty,
                Grid         = grid,
                Latitude     = lat,
                Longitude    = lon,
                SpottedUtc   = spottedUtc == default ? DateTime.UtcNow : spottedUtc,
                Spotter      = r.Callsign ?? string.Empty,
                Comments     = r.Comments ?? string.Empty,
                SpotCount    = 1
            };
        }

        // ---- JSON DTO ----
        private class SotaSpotRaw
        {
            [JsonPropertyName("timeStamp")]         public string? TimeStamp         { get; set; }
            [JsonPropertyName("comments")]          public string? Comments          { get; set; }
            [JsonPropertyName("callsign")]          public string? Callsign          { get; set; }
            [JsonPropertyName("associationCode")]   public string? AssociationCode   { get; set; }
            [JsonPropertyName("summitCode")]        public string? SummitCode        { get; set; }
            [JsonPropertyName("activatorCallsign")] public string? ActivatorCallsign { get; set; }
            [JsonPropertyName("activatorName")]     public string? ActivatorName     { get; set; }
            [JsonPropertyName("frequency")]         public string? Frequency         { get; set; }
            [JsonPropertyName("mode")]              public string? Mode              { get; set; }
            [JsonPropertyName("summitDetails")]     public string? SummitDetails     { get; set; }
            [JsonPropertyName("maidenhead")]        public string? Maidenhead        { get; set; }
        }
    }
}
