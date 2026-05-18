using System.IO.Ports;

namespace xOTACompanion.Services
{
    /// <summary>
    /// ICOM CI-V binary serial protocol radio control service.
    /// Supports IC-7300, IC-705, IC-7610, IC-9700 and other ICOM CI-V radios.
    ///
    /// CI-V frame format:  FE FE [dest] [src=0xE0] [cmd] [data...] FD
    /// Radio response:     FE FE [src=0xE0] [radio_addr] [cmd] [data...] FD
    ///
    /// When connected via USB the IC-7300 echoes every sent byte back; echoed
    /// frames have radio_addr in byte[2] rather than 0xE0, so they are ignored
    /// automatically by the receive filter.
    /// </summary>
    public class CIVService : IRadioService, IDisposable
    {
        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<double, string, int>? RadioInfoUpdated;
        public event Action<bool>? TXModeChanged;

        // CI-V constants
        private const byte CtrlAddr = 0xE0;   // controller (PC) address
        private const byte CmdSetFreq  = 0x05;
        private const byte CmdReadFreq = 0x03;
        private const byte CmdSetMode  = 0x06;
        private const byte CmdReadMode = 0x04;
        private const byte CmdTxStatus = 0x1C;

        private readonly string _portName;
        private readonly int    _baudRate;
        private readonly byte   _radioAddr;

        private SerialPort? _port;
        private bool _isConnected;
        private bool _disposed;
        private CancellationTokenSource? _cts;

        // Receive buffer – filled by DataReceived, drained by ReadLoopAsync
        private readonly List<byte> _rxBuf  = new();
        private readonly object     _rxLock = new();
        private readonly object     _txLock = new();

        // Poll timestamps
        private DateTime _lastFreqPoll = DateTime.MinValue;
        private DateTime _lastModePoll = DateTime.MinValue;
        private DateTime _lastTxPoll   = DateTime.MinValue;

        // Cached radio state
        private double _freq  = 0;
        private string _mode  = "USB";
        private int    _power = 0;
        private bool   _isTx;

        public bool   IsConnected      => _isConnected;
        public double CurrentFrequency => _freq;
        public string CurrentMode      => _mode;
        public int    Power            => _power;
        public bool   IsTransmitting   => _isTx;
        public string DeviceInfo       => $"CI-V ({_portName} @ {_baudRate}, addr 0x{_radioAddr:X2})";

        public (double frequency, string mode, int power) GetRadioInfo() => (_freq, _mode, _power);

        /// <param name="radioAddress">CI-V address of the radio (e.g. 0x94 for IC-7300).</param>
        public CIVService(string portName, int baudRate, byte radioAddress = 0x94)
        {
            _portName  = portName;
            _baudRate  = baudRate;
            _radioAddr = radioAddress;
        }

        // ── Connection ───────────────────────────────────────────────────────

        public async Task<bool> ConnectAsync()
        {
            if (_isConnected) return true;

            if (!SerialPort.GetPortNames().Contains(_portName, StringComparer.OrdinalIgnoreCase))
            {
                Logger.Instance.Log(LogCategory.CAT, $"CI-V: Port {_portName} not found");
                return false;
            }

            try
            {
                _port = new SerialPort(_portName, _baudRate)
                {
                    Handshake    = Handshake.None,
                    Parity       = Parity.None,
                    DataBits     = 8,
                    StopBits     = StopBits.One,
                    ReadTimeout  = 200,
                    WriteTimeout = 500,
                };
                _port.DataReceived += Port_DataReceived;
                _port.Open();
                _isConnected = true;
                _cts = new CancellationTokenSource();
                _ = Task.Run(() => ReadLoopAsync(_cts.Token));
                _ = Task.Run(() => PollLoopAsync(_cts.Token));
                await Task.Delay(100);
                Connected?.Invoke();
                Logger.Instance.Log(LogCategory.CAT,
                    $"CI-V: Connected {_portName}@{_baudRate} addr=0x{_radioAddr:X2}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.CAT, $"CI-V: Connect failed – {ex.Message}");
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

        // ── Radio control ────────────────────────────────────────────────────

        public void SetFrequency(double freqMhz)
        {
            if (!_isConnected)
            {
                Logger.Instance.Log(LogCategory.CAT, "CI-V: SetFrequency skipped – not connected");
                return;
            }
            long hz = (long)(freqMhz * 1_000_000);
            Send(BuildFrame(CmdSetFreq, FrequencyToBcd(hz)));
            _freq = freqMhz;
            Logger.Instance.Log(LogCategory.CAT, $"CI-V: SetFrequency → {freqMhz:F6} MHz");
        }

        public void SetMode(string mode)
        {
            if (!_isConnected)
            {
                Logger.Instance.Log(LogCategory.CAT, "CI-V: SetMode skipped – not connected");
                return;
            }

            // IC-7300 mode bytes for command 0x06
            byte modeByte = mode.ToUpperInvariant() switch
            {
                "LSB"                                 => 0x00,
                "USB"                                 => 0x01,
                "AM"                                  => 0x02,
                "CW"                                  => 0x03,
                "RTTY" or "FSK"                       => 0x04,
                "FM"                                  => 0x05,
                "CWR"                                 => 0x06,
                "RTTYR"                               => 0x07,
                "SSB"                                 => 0x01,   // treat as USB
                "DATA" or "DIGI" or "FT8" or "FT4"   => 0x01,   // USB for digital
                _                                     => 0x01,
            };

            // Cmd 0x06 data: [mode_byte, filter=0x01 (FIL1 – widest, keeps things working)]
            Send(BuildFrame(CmdSetMode, new byte[] { modeByte, 0x01 }));
            _mode = mode;
            Logger.Instance.Log(LogCategory.CAT, $"CI-V: SetMode {mode} → 0x{modeByte:X2}");
        }

        // ── Receive ──────────────────────────────────────────────────────────

        private void Port_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_port == null) return;
                int n = _port.BytesToRead;
                if (n == 0) return;
                byte[] buf = new byte[n];
                _port.Read(buf, 0, n);
                Logger.Instance.Log(LogCategory.CAT, $"CI-V: RX {BitConverter.ToString(buf)}");
                lock (_rxLock)
                {
                    if (_rxBuf.Count > 4096) _rxBuf.Clear();
                    _rxBuf.AddRange(buf);
                }
            }
            catch { }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isConnected)
            {
                List<byte[]>? frames = null;
                lock (_rxLock)
                {
                    frames = ExtractFrames();
                }
                if (frames != null)
                {
                    foreach (var f in frames)
                        ProcessFrame(f);
                }
                await Task.Delay(10, ct).ContinueWith(_ => { });
            }
        }

        /// <summary>Extracts all complete CI-V frames from _rxBuf (call under _rxLock).</summary>
        private List<byte[]> ExtractFrames()
        {
            var result = new List<byte[]>();
            while (_rxBuf.Count >= 5)
            {
                // Find preamble FE FE
                int start = -1;
                for (int i = 0; i < _rxBuf.Count - 1; i++)
                {
                    if (_rxBuf[i] == 0xFE && _rxBuf[i + 1] == 0xFE) { start = i; break; }
                }
                if (start < 0) { _rxBuf.Clear(); break; }
                if (start > 0) _rxBuf.RemoveRange(0, start);

                // Find terminator FD
                int end = _rxBuf.IndexOf(0xFD, 2);
                if (end < 0) break;  // frame not yet complete

                var frame = _rxBuf.GetRange(0, end + 1).ToArray();
                _rxBuf.RemoveRange(0, end + 1);

                // Only accept frames addressed TO us (controller addr 0xE0 at byte[2]).
                // Frames with radio_addr at byte[2] are echoes of our own transmissions.
                if (frame.Length >= 5 && frame[2] == CtrlAddr)
                    result.Add(frame);
            }
            return result;
        }

        private void ProcessFrame(byte[] frame)
        {
            // Frame layout: FE FE E0 [radio_addr] [cmd] [data...] FD
            if (frame.Length < 5) return;
            Logger.Instance.Log(LogCategory.CAT, $"CI-V: Frame ← {BitConverter.ToString(frame)}");

            byte cmd     = frame[4];
            int  dataLen = frame.Length - 6;  // subtract: FE FE addr addr cmd FD

            switch (cmd)
            {
                case CmdReadFreq when dataLen == 5:
                    // Response to "read frequency" (0x03) – 5 BCD bytes at offset 5
                    _freq = BcdToHz(frame, 5) / 1_000_000.0;
                    RadioInfoUpdated?.Invoke(_freq, _mode, _power);
                    break;

                case CmdReadMode when dataLen >= 1:
                    // Response to "read mode" (0x04) – mode byte at offset 5
                    _mode = ModeFromByte(frame[5]);
                    RadioInfoUpdated?.Invoke(_freq, _mode, _power);
                    break;

                case CmdTxStatus when dataLen >= 2 && frame[5] == 0x00:
                    // Response to TX status (0x1C 0x00) – status byte at offset 6
                    bool tx = frame[6] == 0x01;
                    if (tx != _isTx) { _isTx = tx; TXModeChanged?.Invoke(tx); }
                    break;
            }
        }

        // ── Poll loop ────────────────────────────────────────────────────────

        private async Task PollLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _isConnected)
            {
                var now = DateTime.Now;

                if ((now - _lastTxPoll).TotalMilliseconds >= 500)
                {
                    Send(BuildFrame(CmdTxStatus, new byte[] { 0x00 }));
                    _lastTxPoll = now;
                }
                else if ((now - _lastFreqPoll).TotalMilliseconds >= 750)
                {
                    Send(BuildFrame(CmdReadFreq, Array.Empty<byte>()));
                    _lastFreqPoll = now;
                }
                else if ((now - _lastModePoll).TotalMilliseconds >= 1000)
                {
                    Send(BuildFrame(CmdReadMode, Array.Empty<byte>()));
                    _lastModePoll = now;
                }

                await Task.Delay(100, ct).ContinueWith(_ => { });
            }
        }

        // ── Frame building & send ────────────────────────────────────────────

        private void Send(byte[] frame)
        {
            try
            {
                if (_port == null || !_port.IsOpen)
                {
                    Logger.Instance.Log(LogCategory.CAT,
                        $"CI-V: Send skipped (port closed) – {BitConverter.ToString(frame)}");
                    return;
                }
                Logger.Instance.Log(LogCategory.CAT, $"CI-V: TX {BitConverter.ToString(frame)}");
                lock (_txLock)
                {
                    _port.Write(frame, 0, frame.Length);
                    System.Threading.Thread.Sleep(30);  // inter-command guard
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Log(LogCategory.CAT, $"CI-V: Send error – {ex.Message}");
            }
        }

        /// <summary>Builds a CI-V frame: FE FE [radio_addr] [ctrl_addr=0xE0] [cmd] [data...] FD</summary>
        private byte[] BuildFrame(byte cmd, byte[] data)
        {
            var frame = new byte[5 + data.Length + 1];
            frame[0] = 0xFE;
            frame[1] = 0xFE;
            frame[2] = _radioAddr;
            frame[3] = CtrlAddr;
            frame[4] = cmd;
            if (data.Length > 0)
                data.CopyTo(frame, 5);
            frame[^1] = 0xFD;
            return frame;
        }

        // ── BCD frequency encoding (CI-V 5-byte format) ──────────────────────
        //
        // Each byte encodes a pair of decimal digits:
        //   high nibble = more-significant digit of the pair
        //   low  nibble = less-significant digit
        //
        // Byte order (little-endian significance):
        //   byte[0]  –  10 Hz  /  1 Hz
        //   byte[1]  –  1 kHz  /  100 Hz
        //   byte[2]  –  100 kHz / 10 kHz
        //   byte[3]  –  10 MHz  / 1 MHz
        //   byte[4]  –  1 GHz   / 100 MHz
        //
        // Example: 14.074 MHz = 14 074 000 Hz → 00-40-07-14-00

        private static byte[] FrequencyToBcd(long hz)
        {
            return new byte[]
            {
                (byte)(((hz / 10        % 10) << 4) | (hz             % 10)),
                (byte)(((hz / 1_000     % 10) << 4) | (hz / 100       % 10)),
                (byte)(((hz / 100_000   % 10) << 4) | (hz / 10_000    % 10)),
                (byte)(((hz / 10_000_000 % 10) << 4) | (hz / 1_000_000 % 10)),
                (byte)(((hz / 1_000_000_000L % 10) << 4) | (hz / 100_000_000 % 10)),
            };
        }

        private static long BcdToHz(byte[] frame, int offset)
        {
            long hz = 0;
            hz += (long)((frame[offset]     >> 4) & 0xF) * 10;
            hz += (long)( frame[offset]           & 0xF) * 1;
            hz += (long)((frame[offset + 1] >> 4) & 0xF) * 1_000;
            hz += (long)( frame[offset + 1]       & 0xF) * 100;
            hz += (long)((frame[offset + 2] >> 4) & 0xF) * 100_000;
            hz += (long)( frame[offset + 2]       & 0xF) * 10_000;
            hz += (long)((frame[offset + 3] >> 4) & 0xF) * 10_000_000;
            hz += (long)( frame[offset + 3]       & 0xF) * 1_000_000;
            hz += (long)((frame[offset + 4] >> 4) & 0xF) * 1_000_000_000;
            hz += (long)( frame[offset + 4]       & 0xF) * 100_000_000;
            return hz;
        }

        private static string ModeFromByte(byte b) => b switch
        {
            0x00 => "LSB",
            0x01 => "USB",
            0x02 => "AM",
            0x03 => "CW",
            0x04 => "RTTY",
            0x05 => "FM",
            0x06 => "CWR",
            0x07 => "RTTYR",
            _    => "USB",
        };

        // ── IDisposable ──────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
            GC.SuppressFinalize(this);
        }
    }
}
