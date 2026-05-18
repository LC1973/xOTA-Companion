namespace xOTACompanion.Models
{
    /// <summary>
    /// Configuration for a radio connection.
    /// Mirrors GreenLogger_New's RadioConfig but kept self-contained here.
    /// </summary>
    public class RadioConfig
    {
        public int RadioId { get; set; }
        public string FriendlyName { get; set; } = string.Empty;

        /// <summary>"TCI", "CAT", or "None"</summary>
        public string ControlType { get; set; } = "None";
        public bool IsDefault { get; set; }

        // TCI (Expert SDR / TCI Protocol)
        public string TCIHost { get; set; } = "127.0.0.1";
        public int TCIPort { get; set; } = 40001;

        // CAT (serial port) – also used for CI-V port/baud settings
        public string CATPortName { get; set; } = "COM1";
        public int CATBaudRate { get; set; } = 38400;

        // CI-V (ICOM) – CI-V address of the radio in decimal (e.g. 148 = 0x94 for IC-7300)
        public int CIVAddress { get; set; } = 0x94;

        public bool IsValid() => ControlType switch
        {
            "TCI"  => !string.IsNullOrWhiteSpace(TCIHost) && TCIPort > 0,
            "CAT"  => !string.IsNullOrWhiteSpace(CATPortName) && CATBaudRate > 0,
            "CIV"  => !string.IsNullOrWhiteSpace(CATPortName) && CATBaudRate > 0,
            "None" => true,
            _ => false
        };

        public override string ToString() =>
            $"{FriendlyName}  ({ControlType})";
    }
}
