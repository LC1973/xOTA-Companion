using System.Net.WebSockets;
using System.Text;

namespace xOTACompanion.Services
{
    /// <summary>
    /// TCI (Transceiver Control Interface) radio service.
    /// Connects to Expert SDR / other TCI-compatible radio software via WebSocket.
    /// Simplified from GreenLogger_New — focused on frequency/mode read and write.
    /// TCI Protocol v1.x: commands end with semicolons, responses are plain text.
    /// </summary>
    public class TCIService : IRadioService, IDisposable
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<double, string, int>? RadioInfoUpdated;
        public event Action<bool>? TXModeChanged;

        private readonly string _host;
        private readonly int _port;
        private ClientWebSocket? _ws;
        private bool _isConnected;
        private bool _disposed;
        private CancellationTokenSource? _cts;

        private double _freq = 14.0;
        private string _mode = "USB";
        private int _power = 0;
        private bool _isTx;

        public bool IsConnected => _isConnected;
        public double CurrentFrequency => _freq;
        public string CurrentMode => _mode;
        public int Power => _power;
        public bool IsTransmitting => _isTx;
        public string DeviceInfo => $"TCI ({_host}:{_port})";

        public (double frequency, string mode, int power) GetRadioInfo() => (_freq, _mode, _power);

        public TCIService(string host = "127.0.0.1", int port = 40001)
        {
            _host = host;
            _port = port;
        }

        public async Task<bool> ConnectAsync()
        {
            if (_isConnected) return true;
            try
            {
                var uri = new Uri($"ws://{_host}:{_port}");
                _ws = new ClientWebSocket();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ws.ConnectAsync(uri, connectCts.Token);

                if (_ws.State != WebSocketState.Open)
                {
                    Logger.Instance.Log(LogCategory.TCI, "TCI: WebSocket did not open");
                    return false;
                }

                _isConnected = true;
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

                // Request current state
                await SendAsync("ready;");
                await Task.Delay(100);
                await SendAsync("vfo:0,0;");
                await Task.Delay(50);
                await SendAsync("modulation:0;");

                Connected?.Invoke();
                Logger.Instance.Log(LogCategory.TCI, $"TCI: Connected to {_host}:{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.TCI, $"TCI: Connect failed – {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        public void Disconnect()
        {
            if (!_isConnected) return;
            _isConnected = false;
            _cts?.Cancel();
            try { _ws?.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(1000); } catch { }
            _ws?.Dispose();
            _ws = null;
            Disconnected?.Invoke();
        }

        public void SetFrequency(double freqMhz)
        {
            if (!_isConnected) return;
            long hz = (long)(freqMhz * 1_000_000);
            _ = SendAsync($"vfo:0,0,{hz};");
            _freq = freqMhz;
        }

        public void SetMode(string mode)
        {
            if (!_isConnected) return;
            // TCI uses mode strings directly (USB, LSB, CW, AM, FM, DIGI, etc.)
            string tciMode = mode.ToUpperInvariant() switch
            {
                "FT8" or "FT4" or "DATA" or "DIGI" or "RTTY" => "DIGI",
                "CWR" => "CW_R",
                _ => mode.ToUpperInvariant()
            };
            _ = SendAsync($"modulation:0,{tciMode};");
            _mode = mode;
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            var buf = new byte[4096];
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var msg = Encoding.UTF8.GetString(buf, 0, result.Count).Trim();
                        ProcessMessage(msg);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _isConnected = false;
                        Disconnected?.Invoke();
                        break;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (_ws?.State != WebSocketState.Open)
                    {
                        _isConnected = false;
                        Disconnected?.Invoke();
                        break;
                    }
                    Logger.Instance.Log(LogCategory.TCI, $"TCI: Receive error – {ex.Message}");
                    await Task.Delay(500, ct).ContinueWith(_ => { });
                }
            }
        }

        private void ProcessMessage(string msg)
        {
            // vfo:0,0,14195000;
            if (msg.StartsWith("vfo:0,0,"))
            {
                var part = msg[8..].TrimEnd(';');
                if (long.TryParse(part, out long hz))
                {
                    _freq = hz / 1_000_000.0;
                    RadioInfoUpdated?.Invoke(_freq, _mode, _power);
                }
            }
            // modulation:0,USB;
            else if (msg.StartsWith("modulation:0,"))
            {
                _mode = msg[13..].TrimEnd(';');
                RadioInfoUpdated?.Invoke(_freq, _mode, _power);
            }
            // drive:0,50;
            else if (msg.StartsWith("drive:0,"))
            {
                if (int.TryParse(msg[8..].TrimEnd(';'), out int pct))
                {
                    _power = pct;
                    RadioInfoUpdated?.Invoke(_freq, _mode, _power);
                }
            }
            // trx:0,true;  / trx:0,false;
            else if (msg.StartsWith("trx:0,"))
            {
                bool tx = msg.Contains("true");
                if (tx != _isTx) { _isTx = tx; TXModeChanged?.Invoke(tx); }
            }
        }

        private async Task SendAsync(string command)
        {
            try
            {
                if (_ws?.State != WebSocketState.Open) return;
                var bytes = Encoding.UTF8.GetBytes(command);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
                Logger.Instance.Log(LogCategory.TCI, $"TCI: >>> {command}");
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.TCI, $"TCI: Send error – {ex.Message}");
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
