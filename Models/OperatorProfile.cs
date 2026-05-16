namespace xOTACompanion.Models
{
    /// <summary>
    /// Operator / station profile. Each profile represents a callsign with a known location.
    /// </summary>
    public class OperatorProfile
    {
        public string Callsign { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Locator { get; set; } = string.Empty;   // Maidenhead grid (6-char recommended)
        public string SotaApiKey { get; set; } = string.Empty; // SOTAwatch Bearer token
        public bool IsDefault { get; set; }

        public override string ToString() =>
            !string.IsNullOrWhiteSpace(Name)
                ? $"{Callsign}  ({Name})"
                : Callsign;
    }
}
