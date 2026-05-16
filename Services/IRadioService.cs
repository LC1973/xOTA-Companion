namespace xOTACompanion.Services
{
    /// <summary>
    /// Common interface for all radio control services.
    /// Extends the GreenLogger_New IRadioService with frequency/mode setting.
    /// </summary>
    public interface IRadioService
    {
        // Connection state
        bool IsConnected { get; }

        // Radio state (read)
        double CurrentFrequency { get; }
        string CurrentMode { get; }
        int Power { get; }
        bool IsTransmitting { get; }
        string DeviceInfo { get; }

        // Events
        event Action? Connected;
        event Action? Disconnected;
        event Action<double, string, int>? RadioInfoUpdated;  // freq MHz, mode, power W
        event Action<bool>? TXModeChanged;

        // Connection
        Task<bool> ConnectAsync();
        void Disconnect();

        // Radio control (write)
        void SetFrequency(double frequencyMhz);
        void SetMode(string mode);

        (double frequency, string mode, int power) GetRadioInfo();
    }
}
