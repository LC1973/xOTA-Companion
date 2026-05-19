using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using xOTACompanion.Models;

namespace xOTACompanion.Services
{
    /// <summary>
    /// Fetches active WWBOTA/UKBOTA spots and posts self-spots via api.wwbota.net
    /// </summary>
    public class WwbotaService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private const string BaseUrl    = "https://api.wwbota.net";
        private const string SpotsUrl   = BaseUrl + "/spots/";
        private const string BunkersUrl = BaseUrl + "/bunkers/";

        // Simple in-memory bunker cache (reference → name/locator) to avoid
        // fetching the full list on every spot refresh.
        private static readonly Dictionary<string, WwbotaBunkerRaw> _bunkerCache = new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _bunkerCacheTime = DateTime.MinValue;
        private static readonly TimeSpan BunkerCacheTtl = TimeSpan.FromHours(6);

        // ── Spot fetching ─────────────────────────────────────────────────────

        /// <summary>Fetch active WWBOTA spots (default: last 1 hour).</summary>
        public async Task<List<SpotModel>> FetchSpotsAsync(int ageHours = 1)
        {
            try
            {
                await EnsureBunkerCacheAsync();

                var url  = $"{SpotsUrl}?age={Math.Clamp(ageHours, 0, 24)}";
                var json = await _http.GetStringAsync(url);
                var raw  = JsonSerializer.Deserialize<List<WwbotaSpotRaw>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (raw == null) return new();

                return raw
                    .Where(r => !string.IsNullOrWhiteSpace(r.Call))
                    .SelectMany(MapSpots)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.WWBOTA, $"WwbotaService: FetchSpots – {ex.Message}");
                return new();
            }
        }

        /// <summary>
        /// Post a self-spot to WWBOTA.
        /// No authentication is required for posting spots.
        /// The bunker reference must appear in the comment field.
        /// </summary>
        public async Task<(bool Ok, string Message)> PostSelfSpotAsync(
            string callsign, string reference, double freqMhz, string mode, string comment = "")
        {
            try
            {
                // WWBOTA requires the reference to be present in the comment
                var safeRef = reference.Trim().ToUpperInvariant();
                var body    = comment.Trim();
                if (!body.Contains(safeRef, StringComparison.OrdinalIgnoreCase))
                    body = string.IsNullOrWhiteSpace(body) ? safeRef : $"{safeRef} {body}";

                var payload = new
                {
                    call    = callsign.Trim().ToUpperInvariant(),
                    spotter = callsign.Trim().ToUpperInvariant(),
                    freq    = freqMhz,
                    mode    = mode.Trim().ToUpperInvariant(),
                    comment = body
                };

                var json    = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp    = await _http.PostAsync(SpotsUrl, content);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                    return (true, $"WWBOTA spot posted for {safeRef} on {freqMhz:F3} MHz {mode}");

                Logger.Instance.Log(LogCategory.WWBOTA, $"WwbotaService: POST {(int)resp.StatusCode} – {respBody}");
                return (false, $"WWBOTA spot failed ({(int)resp.StatusCode}): {respBody}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.WWBOTA, $"WwbotaService: PostSelfSpot – {ex.Message}");
                return (false, $"WWBOTA spot error: {ex.Message}");
            }
        }

        // ── Bunker cache ──────────────────────────────────────────────────────

        private async Task EnsureBunkerCacheAsync()
        {
            if (_bunkerCache.Count > 0 && DateTime.UtcNow - _bunkerCacheTime < BunkerCacheTtl)
                return;
            try
            {
                var json = await _http.GetStringAsync(BunkersUrl);
                var list = JsonSerializer.Deserialize<List<WwbotaBunkerRaw>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (list == null) return;

                _bunkerCache.Clear();
                foreach (var b in list)
                    if (!string.IsNullOrWhiteSpace(b.Reference))
                        _bunkerCache[b.Reference] = b;
                _bunkerCacheTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.WWBOTA, $"WwbotaService: BunkerCache – {ex.Message}");
            }
        }

        // ── Mapping ───────────────────────────────────────────────────────────

        /// <summary>
        /// A single API spot can reference multiple bunkers; expand to one SpotModel per reference.
        /// </summary>
        private static IEnumerable<SpotModel> MapSpots(WwbotaSpotRaw r)
        {
            // Pull references from the embedded references array if present,
            // otherwise fall back to parsing the comment string.
            var refs = r.References?.Where(x => !string.IsNullOrWhiteSpace(x.Reference)).ToList()
                    ?? new List<WwbotaRefRaw>();

            if (refs.Count == 0)
            {
                // Try to extract references from the comment (e.g. "B/G-2114,B/G-2115")
                if (!string.IsNullOrWhiteSpace(r.Comment))
                {
                    foreach (var tok in r.Comment.Split(' ', ',', ';'))
                    {
                        if (tok.StartsWith("B/", StringComparison.OrdinalIgnoreCase))
                        {
                            _bunkerCache.TryGetValue(tok.Trim().ToUpperInvariant(), out var b);
                            refs.Add(new WwbotaRefRaw
                            {
                                Reference = tok.Trim().ToUpperInvariant(),
                                Name      = b?.Name,
                                Locator   = b?.Locator,
                                Lat       = b?.Lat,
                                Long      = b?.Long
                            });
                        }
                    }
                }
                if (refs.Count == 0) yield break;
            }

            DateTime.TryParse(r.Time, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTime spotTime);

            foreach (var refRaw in refs)
            {
                // Prefer coordinates from the embedded reference; fall back to bunker cache
                _bunkerCache.TryGetValue(refRaw.Reference ?? string.Empty, out var cached);
                double? lat     = refRaw.Lat     ?? cached?.Lat;
                double? lon     = refRaw.Long    ?? cached?.Long;
                string  locator = refRaw.Locator ?? cached?.Locator ?? string.Empty;
                string  name    = refRaw.Name    ?? cached?.Name    ?? string.Empty;
                string  type    = cached?.Type   ?? string.Empty;

                // Derive lat/lon from Maidenhead locator if coords missing
                if ((!lat.HasValue || !lon.HasValue) && locator.Length >= 4)
                {
                    var (gLat, gLon) = MaidenheadService.LocatorToCoordinates(locator);
                    lat = gLat;
                    lon = gLon;
                }

                // Build a descriptive name including the bunker type
                string displayName = string.IsNullOrWhiteSpace(type)
                    ? name
                    : string.IsNullOrWhiteSpace(name) ? type : $"{name}  [{type}]";

                yield return new SpotModel
                {
                    Source       = SpotSource.WWBOTA,
                    Activator    = r.Call ?? string.Empty,
                    Reference    = refRaw.Reference ?? string.Empty,
                    Name         = displayName,
                    LocationDesc = refRaw.Reference != null
                                   ? SchemeFromReference(refRaw.Reference)
                                   : string.Empty,
                    FrequencyMhz = r.Freq ?? 0.0,
                    Mode         = r.Mode ?? string.Empty,
                    Grid         = locator,
                    Latitude     = lat,
                    Longitude    = lon,
                    SpottedUtc   = spotTime,
                    Spotter      = r.Spotter ?? string.Empty,
                    Comments     = r.Comment ?? string.Empty,
                    SpotCount    = 1
                };
            }
        }

        /// <summary>Derive a human-readable scheme label from the reference prefix (e.g. B/G → England).</summary>
        private static string SchemeFromReference(string reference)
        {
            // Reference format: B/{prefix}-{number}
            if (!reference.StartsWith("B/", StringComparison.OrdinalIgnoreCase) || reference.Length < 4)
                return string.Empty;
            var prefix = reference[2..].Split('-')[0].ToUpperInvariant();
            return prefix switch
            {
                "G"  or "M"  => "England",
                "GM" or "MM" => "Scotland",
                "GW" or "MW" => "Wales",
                "GI" or "MI" => "N. Ireland",
                "GJ"         => "Jersey",
                "GU"         => "Guernsey",
                "GD"         => "Isle of Man",
                "ON"         => "Belgium",
                "OK"         => "Czech Republic",
                "F"          => "France",
                "LA"         => "Norway",
                "I"          => "Italy",
                "EI"         => "Ireland",
                "S5"         => "Slovenia",
                _            => string.Empty
            };
        }

        // ── Raw API models ────────────────────────────────────────────────────

        private class WwbotaSpotRaw
        {
            public string?              Call       { get; set; }
            public string?              Comment    { get; set; }
            [JsonPropertyName("freq")]
            public double?              Freq       { get; set; }
            public string?              Mode       { get; set; }
            public List<WwbotaRefRaw>?  References { get; set; }
            public string?              Spotter    { get; set; }
            public string?              Time       { get; set; }
            public string?              Type       { get; set; }
        }

        private class WwbotaRefRaw
        {
            public string?  Reference { get; set; }
            public string?  Name      { get; set; }
            public string?  Locator   { get; set; }
            public double?  Lat       { get; set; }
            public double?  Long      { get; set; }
        }

        private class WwbotaBunkerRaw
        {
            public string?  Reference { get; set; }
            public string?  Name      { get; set; }
            public string?  Locator   { get; set; }
            public double?  Lat       { get; set; }
            public double?  Long      { get; set; }
            public string?  Type      { get; set; }
            public string?  Scheme    { get; set; }
        }
    }
}
