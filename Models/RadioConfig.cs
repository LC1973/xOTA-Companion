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

        // CI-V (ICOM) – CI-V address of the radio in hex (e.g. 0x94 IC-7300, 0xA4 IC-705,
        // 0xA2 IC-9700, 0xE3 IC-7610). 0 = unset; the user must pick one in the edit dialog.
        public int CIVAddress { get; set; } = 0;

        public bool IsValid() => ControlType switch
        {
            "TCI"  => !string.IsNullOrWhiteSpace(TCIHost) && TCIPort > 0,
            "CAT"  => !string.IsNullOrWhiteSpace(CATPortName) && CATBaudRate > 0,
            // CI-V addresses are 1 byte; 0x00 is broadcast and 0xE0/0xFE/0xFF are reserved
            // for the controller/preamble. Valid radio addresses are 0x01–0xDF.
            "CIV"  => !string.IsNullOrWhiteSpace(CATPortName) && CATBaudRate > 0
                       && CIVAddress >= 0x01 && CIVAddress <= 0xDF,
            "None" => true,
            _ => false
        };

        public override string ToString() =>
            $"{FriendlyName}  ({ControlType})";
    }
}
