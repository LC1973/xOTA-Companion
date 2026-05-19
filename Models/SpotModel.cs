namespace xOTACompanion.Models
{
    /// <summary>
    /// Unified spot model covering both POTA and SOTA spots.
    /// </summary>
    public class SpotModel
    {
        // --- Source info ---
        public SpotSource Source { get; set; }

        // --- Activator / station ---
        public string Activator { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;  // Park name (POTA) or summit details (SOTA)
        public string Reference { get; set; } = string.Empty;   // e.g. K-0001 or G/SP-001
        public string LocationDesc { get; set; } = string.Empty; // e.g. "Missouri" or "England"

        // --- Radio info ---
        public double FrequencyMhz { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string Band => FrequencyToband(FrequencyMhz);

        // --- Location ---
        public string Grid { get; set; } = string.Empty;       // Maidenhead grid (4 or 6 char)
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // --- Spot metadata ---
        public DateTime SpottedUtc { get; set; }
        public string Spotter { get; set; } = string.Empty;
        public string Comments { get; set; } = string.Empty;
        public int SpotCount { get; set; }   // POTA: number of spots

        // --- Computed ---
        public double? DistanceKm { get; set; }
        public string DistanceUnit { get; set; } = "km";
        public double? DistanceMiles => DistanceKm.HasValue ? DistanceKm.Value * 0.621371 : null;

        public string AgeDisplay
        {
            get
            {
                var age = DateTime.UtcNow - SpottedUtc;
                if (age.TotalMinutes < 1) return "just now";
                if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
                if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
                return SpottedUtc.ToLocalTime().ToString("HH:mm");
            }
        }

        public string FrequencyDisplay => FrequencyMhz > 0 ? $"{FrequencyMhz:F3}" : "-";

        public string DistanceDisplay
        {
            get
            {
                if (!DistanceKm.HasValue) return "-";
                return DistanceUnit == "mi"
                    ? $"{DistanceKm.Value:N0} mi"
                    : $"{DistanceKm.Value:N0} km";
            }
        }

        public string SourceIcon => Source switch
        {
            SpotSource.POTA   => "\U0001F333",  // 🌳
            SpotSource.SOTA   => "\u26F0",       // ⛰
            SpotSource.WWBOTA => "\U0001F3F0",  // 🏰
            _                 => "?"
        };

        public bool HasLocation => (!string.IsNullOrWhiteSpace(Grid) && Grid.Length >= 4)
                                || (Latitude.HasValue && Longitude.HasValue);

        private static string FrequencyToband(double mhz) => mhz switch
        {
            >= 1.8 and < 2.0    => "160m",
            >= 3.5 and < 4.0    => "80m",
            >= 5.0 and < 5.5    => "60m",
            >= 7.0 and < 7.3    => "40m",
            >= 10.1 and < 10.15 => "30m",
            >= 14.0 and < 14.35 => "20m",
            >= 18.068 and < 18.168 => "17m",
            >= 21.0 and < 21.45 => "15m",
            >= 24.89 and < 24.99 => "12m",
            >= 28.0 and < 29.7  => "10m",
            >= 50.0 and < 54.0  => "6m",
            >= 144.0 and < 148.0 => "2m",
            >= 430.0 and < 440.0 => "70cm",
            _ => "?"
        };
    }

    public enum SpotSource { POTA, SOTA, WWBOTA }
}
