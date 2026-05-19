using System.IO;

namespace xOTACompanion.Services
{
    public enum LogCategory { General, Radio, CAT, TCI, POTA, SOTA, WWBOTA, Map, UI }

    /// <summary>Simple file + in-memory logger.</summary>
    public class Logger
    {
        private static readonly Lazy<Logger> _inst = new(() => new Logger());
        public static Logger Instance => _inst.Value;

        private readonly object _lock = new();
        private StreamWriter? _writer;
        private readonly List<string> _buffer = new();
        public event Action<string>? LogAdded;

        private Logger()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "xOTA Companion");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "xota_companion.log");
                _writer = new StreamWriter(path, append: true) { AutoFlush = true };
                _writer.WriteLine($"--- xOTA Companion started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
            }
            catch { /* logging must not crash the app */ }
        }

        public void Log(LogCategory cat, string message)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{cat}] {message}";
            lock (_lock)
            {
                _buffer.Add(line);
                if (_buffer.Count > 5000) _buffer.RemoveAt(0);
                try { _writer?.WriteLine(line); } catch { }
            }
            LogAdded?.Invoke(line);
        }
    }
}
