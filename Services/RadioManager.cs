namespace xOTACompanion.Services
{
    /// <summary>
    /// Singleton manager for the active radio service.
    /// Ported from GreenLogger_New RadioManager.
    /// </summary>
    public class RadioManager
    {
        private static readonly Lazy<RadioManager> _inst = new(() => new RadioManager());
        public static RadioManager Instance => _inst.Value;

        private IRadioService? _active;
        private readonly object _lock = new();

        public IRadioService? ActiveRadio { get { lock (_lock) return _active; } }
        public bool IsConnected => ActiveRadio?.IsConnected == true;
        public double CurrentFrequency => ActiveRadio?.CurrentFrequency ?? 0;
        public string CurrentMode => ActiveRadio?.CurrentMode ?? string.Empty;
        public int Power => ActiveRadio?.Power ?? 0;
        public string DeviceInfo => ActiveRadio?.DeviceInfo ?? string.Empty;

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<double, string, int>? RadioInfoUpdated;
        public event Action<bool>? TXModeChanged;

        private RadioManager() { }

        public void SetActiveRadio(IRadioService? radio)
        {
            lock (_lock)
            {
                if (_active != null)
                {
                    _active.Connected        -= OnConnected;
                    _active.Disconnected     -= OnDisconnected;
                    _active.RadioInfoUpdated -= OnRadioInfoUpdated;
                    _active.TXModeChanged    -= OnTXModeChanged;
                }

                _active = radio;

                if (_active != null)
                {
                    _active.Connected        += OnConnected;
                    _active.Disconnected     += OnDisconnected;
                    _active.RadioInfoUpdated += OnRadioInfoUpdated;
                    _active.TXModeChanged    += OnTXModeChanged;
                }
            }
        }

        public void SetFrequency(double mhz)
        {
            lock (_lock) { _active?.SetFrequency(mhz); }
        }

        public void SetMode(string mode)
        {
            lock (_lock) { _active?.SetMode(mode); }
        }

        public void TuneToSpot(double freqMhz, string mode)
        {
            lock (_lock)
            {
                if (_active == null || !_active.IsConnected) return;
                _active.SetFrequency(freqMhz);
                if (!string.IsNullOrWhiteSpace(mode))
                    _active.SetMode(mode);
                Logger.Instance.Log(LogCategory.Radio,
                    $"RadioManager: Tune → {freqMhz:F3} MHz {mode}");
            }
        }

        private void OnConnected()        => Connected?.Invoke();
        private void OnDisconnected()     => Disconnected?.Invoke();
        private void OnRadioInfoUpdated(double f, string m, int p) => RadioInfoUpdated?.Invoke(f, m, p);
        private void OnTXModeChanged(bool tx) => TXModeChanged?.Invoke(tx);
    }
}
