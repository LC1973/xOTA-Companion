using xOTACompanion.Models;

namespace xOTACompanion.Models
{
    /// <summary>
    /// Root application configuration — serialised to JSON in %APPDATA%\xOTA Companion\config.json
    /// </summary>
    public class AppConfig
    {
        public List<OperatorProfile> Operators { get; set; } = new();
        public List<RadioConfig> Radios { get; set; } = new();

        public string ActiveOperatorCallsign { get; set; } = string.Empty;
        public int ActiveRadioId { get; set; } = 0;

        // Manual station config (used when GreenLogger is not available)
        public string MyCallsign { get; set; } = string.Empty;
        public string MyLocator  { get; set; } = string.Empty;

        // Mapbox
        public string MapboxAccessToken { get; set; } = string.Empty;

        // Spot fetching
        public int AutoRefreshMinutes { get; set; } = 2;
        public bool ShowPota  { get; set; } = true;
        public bool ShowSota  { get; set; } = true;   // default on; user may override in saved config
        public bool ShowWwbota { get; set; } = true;

        // Display
        public string DistanceUnit { get; set; } = "km";  // "km" or "mi"

        // Window position / size
        public double WindowLeft   { get; set; } = 100;
        public double WindowTop    { get; set; } = 100;
        public double WindowWidth  { get; set; } = 1200;
        public double WindowHeight { get; set; } = 700;

        /// <summary>True when operators/radios were loaded from the GreenLogger DB.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool LoadedFromGreenLogger { get; set; } = false;
    }
}
