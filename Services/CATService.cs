using System.IO.Ports;
using System.Text;

namespace xOTACompanion.Services
{
    /// <summary>
    /// CAT (serial port) radio control service.
    /// Supports Yaesu FTX-1 and generic Yaesu CAT protocol.
    /// Ported and simplified from GreenLogger_New CATService.
    /// </summary>
    public class CATService : IRadioService, IDisposable
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<double, string, int>? RadioInfoUpdated;
        public event Action<bool>? TXModeChanged;

        private readonly string _portName;
        private readonly int _baudRate;
        private SerialPort? _port;
        private bool _isConnected;
        private bool _disposed;
        private CancellationTokenSource? _cts;

        private readonly StringBuilder _rxBuf = new();
        private readonly object _rxLock = new();
        private readonly object _sendLock = new();

        // Polling timestamps
        private DateTime _lastFA = DateTime.MinValue;
        private DateTime _lastMD = DateTime.MinValue;
        private DateTime _lastTX = DateTime.MinValue;

        // Cached state
        private double _freq = 0;
        private string _mode = "USB";
        private int _power = 0;
        private bool _isTx;

        public bool IsConnected => _isConnected;
        public double CurrentFrequency => _freq;
        public string CurrentMode => _mode;
        public int Power => _power;
        public bool IsTransmitting => _isTx;
        public string DeviceInfo => $"CAT ({_portName} @ {_baudRate})";

        public (double frequency, string mode, int power) GetRadioInfo() => (_freq, _mode, _power);

        public CATService(string portName, int baudRate = 38400)
        {
            _portName = portName;
            _baudRate = baudRate;
        }

        public async Task<bool> ConnectAsync()
        {
            if (_isConnected) return true;

            var ports = SerialPort.GetPortNames();
            if (!ports.Contains(_portName, StringComparer.OrdinalIgnoreCase))
            {
                Logger.Instance.Log(LogCategory.CAT, $"CAT: Port {_portName} not found");
                return false;
            }

            try
            {
                _port = new SerialPort(_portName, _baudRate)
                {
                    Handshake = Handshake.None,
                    Parity = Parity.None,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    ReadTimeout = 50,
                    WriteTimeout = 500,
                    Encoding = Encoding.ASCII
                };
                _port.DataReceived += Port_DataReceived;
                _port.Open();
                _isConnected = true;
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ReadLoopAsync(_cts.Token));
                _ = Task.Run(() => PollLoopAsync(_cts.Token));
                await Task.Delay(100);
                Connected?.Invoke();
                Logger.Instance.Log(LogCategory.CAT, $"CAT: Connected {_portName}@{_baudRate}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.CAT, $"CAT: Connect failed – {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;
            _cts?.Cancel();
            try { _port?.Close(); } catch { }
            _port?.Dispose();
            _port = null;
            Disconnected?.Invoke();
        }

        public void SetFrequency(double freqMhz)
        {
            if (!_isConnected) { Logger.Instance.Log(LogCategory.CAT, "CAT: SetFrequency skipped – not connected"); return; }
            long hz = (long)(freqMhz * 1_000_000);
            var cmd = $"FA{hz:D9};";
            Logger.Instance.Log(LogCategory.CAT, $"CAT: SetFrequency → {cmd}");
            Send(cmd);
            _freq = freqMhz;
        }

        public void SetMode(string mode)
        {
            if (!_isConnected) { Logger.Instance.Log(LogCategory.CAT, "CAT: SetMode skipped – not connected"); return; }
            string cmd = mode.ToUpperInvariant() switch
            {
                "LSB"  => "MD01;",
                "USB"  => "MD02;",
                "CW"   => "MD03;",
                "FM"   => "MD04;",
                "AM"   => "MD05;",
                "RTTY" or "FSK"  => "MD06;",
                "CWR"  => "MD07;",
                "DATA" or "DIGI" => "MD08;",
                "FT8"  or "FT4"  => "MD0C;",
                "SSB"  => "MD02;",
                _ => "MD02;"
            };
            Logger.Instance.Log(LogCategory.CAT, $"CAT: SetMode {mode} → {cmd}");
            Send(cmd);
            _mode = mode;
            // Re-send after 200 ms to override band-memory recall that the FTX-1
            // triggers internally after a frequency change.
            var capturedCmd = cmd;
            var capturedMode = mode;
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                if (_isConnected)
                {
                    Logger.Instance.Log(LogCategory.CAT, $"CAT: SetMode re-send {capturedMode} → {capturedCmd}");
                    Send(capturedCmd);
                }
            });
        }

        // --- Private helpers ---

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_port == null) return;
                int n = _port.BytesToRead;
                if (n == 0) return;
                byte[] buf = new byte[n];
                _port.Read(buf, 0, n);
                var raw = Encoding.ASCII.GetString(buf);
                Logger.Instance.Log(LogCategory.CAT, $"CAT: RX raw: {raw.Replace("\r", "\\r").Replace("\n", "\\n")}");
                lock (_rxLock)
                {
                    if (_rxBuf.Length > 8192) _rxBuf.Clear();
                    foreach (var b in buf) _rxBuf.Append((char)b);
                }
            }
            catch { }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isConnected)
            {
                lock (_rxLock)
                {
                    var s = _rxBuf.ToString();
                    int idx = s.IndexOf(';');
                    while (idx >= 0)
                    {
                        string frame = s[..(idx + 1)];
                        _rxBuf.Remove(0, idx + 1);
                        s = _rxBuf.ToString();
                        ProcessFrame(frame);
                        idx = s.IndexOf(';');
                    }
                }
                await Task.Delay(10, ct).ContinueWith(_ => { });
            }
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isConnected)
            {
                var now = DateTime.Now;
                if ((now - _lastTX).TotalMilliseconds >= 500)  { Send("TX;");  _lastTX = now; }
                else if ((now - _lastFA).TotalMilliseconds >= 750) { Send("FA;");  _lastFA = now; }
                else if ((now - _lastMD).TotalMilliseconds >= 1000) { Send("MD0;"); _lastMD = now; }
                await Task.Delay(100, ct).ContinueWith(_ => { });
            }
        }

        private void ProcessFrame(string frame)
        {
            Logger.Instance.Log(LogCategory.CAT, $"CAT: Frame: {frame}");
            try
            {
                if (frame.StartsWith("FA") && frame.Length > 4)
                {
                    if (long.TryParse(frame[2..^1], out long hz))
                    {
                        _freq = hz / 1_000_000.0;
                        RadioInfoUpdated?.Invoke(_freq, _mode, _power);
                    }
                }
                else if (frame.StartsWith("MD") && frame.Length >= 4)
                {
                    _mode = frame[3] switch
                    {
                        '1' => "LSB", '2' => "USB", '3' => "CW", '4' => "FM",
                        '5' => "AM",  '6' => "RTTY", '7' => "CWR", '8' => "DATA",
                        'C' => "DATA", _ => "USB"
                    };
                    RadioInfoUpdated?.Invoke(_freq, _mode, _power);
                }
                else if (frame.StartsWith("TX"))
                {
                    bool tx = frame.Length > 2 && frame[2] == '1';
                    if (tx != _isTx) { _isTx = tx; TXModeChanged?.Invoke(tx); }
                }
            }
            catch { }
        }

        private void Send(string cmd)
        {
            try
            {
                if (_port == null || !_port.IsOpen)
                {
                    Logger.Instance.Log(LogCategory.CAT, $"CAT: Send skipped (port closed) – {cmd}");
                    return;
                }
                lock (_sendLock)
                {
                    _port.Write(Encoding.ASCII.GetBytes(cmd), 0, cmd.Length);
                    System.Threading.Thread.Sleep(50); // inter-command guard (FTX-1 requires ~50ms)
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.CAT, $"CAT: Send error – {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            GC.SuppressFinalize(this);
        }
    }
}
