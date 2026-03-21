#nullable enable
using System;
using System.IO;

namespace PowerTerminal.Services
{
    /// <summary>
    /// Provides separate log files for terminal, AI, and wiki interactions.
    /// Keeps one <see cref="StreamWriter"/> open per category and rotates daily,
    /// avoiding the open/write/close overhead of <see cref="File.AppendAllText"/> on every call.
    /// </summary>
    public class LoggingService : IDisposable
    {
        private readonly string _logDir;
        private readonly object _terminalLock = new();
        private readonly object _aiLock = new();
        private readonly object _wikiLock = new();

        // Per-category writers and the date they were opened for.
        private StreamWriter? _terminalWriter; private string _terminalDate = string.Empty;
        private StreamWriter? _aiWriter;       private string _aiDate       = string.Empty;
        private StreamWriter? _wikiWriter;     private string _wikiDate     = string.Empty;

        private bool _disposed;

        public bool EnableDebugLogging { get; set; }

        public LoggingService(string logDir)
        {
            _logDir = logDir;
            Directory.CreateDirectory(logDir);
        }

        // ── Terminal logging ──────────────────────────────────────────────────────

        public void LogTerminalInput(string sessionName, string data)
        {
            if (EnableDebugLogging)
                WriteLog("terminal", $"[{Ts()}] [{sessionName}] INPUT: {data}");
        }

        public void LogTerminalOutput(string sessionName, string data)
        {
            if (EnableDebugLogging)
                WriteLog("terminal", $"[{Ts()}] [{sessionName}] OUTPUT: {data}");
        }

        public void LogTerminalEvent(string sessionName, string message)
            => WriteLog("terminal", $"[{Ts()}] [{sessionName}] {message}");

        // ── AI logging ────────────────────────────────────────────────────────────

        public void LogAiMessage(string role, string content, string? model = null)
        {
            string modelTag = model != null ? $" (model:{model})" : "";
            WriteLog("ai", $"[{Ts()}]{modelTag} [{role.ToUpperInvariant()}]: {content}");
        }

        public void LogAiError(string error)
            => WriteLog("ai", $"[{Ts()}] [ERROR]: {error}");

        // ── Wiki logging ──────────────────────────────────────────────────────────

        public void LogWikiSearch(string query, int resultCount)
            => WriteLog("wiki", $"[{Ts()}] SEARCH: \"{query}\" -> {resultCount} results");

        public void LogWikiCommandCopied(string wikiTitle, string command)
            => WriteLog("wiki", $"[{Ts()}] COPY: [{wikiTitle}] {command}");

        public void LogWikiCommandExecuted(string wikiTitle, string command)
            => WriteLog("wiki", $"[{Ts()}] EXEC: [{wikiTitle}] {command}");

        public void LogWikiEdited(string action, string wikiTitle)
            => WriteLog("wiki", $"[{Ts()}] {action.ToUpperInvariant()}: {wikiTitle}");

        // ── Internal helpers ──────────────────────────────────────────────────────

        private static string Ts() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        private string LogFilePath(string category, string date)
            => Path.Combine(_logDir, $"{category}_{date}.log");

        private void WriteLog(string category, string line)
        {
            object locker = category switch
            {
                "terminal" => _terminalLock,
                "ai"       => _aiLock,
                "wiki"     => _wikiLock,
                _          => _terminalLock
            };

            lock (locker)
            {
                if (_disposed) return;
                try
                {
                    var writer = GetOrCreateWriter(category);
                    writer.WriteLine(line);
                }
                catch
                {
                    // ignore logging failures
                }
            }
        }

        /// <summary>
        /// Returns (and if necessary creates/rotates) the open <see cref="StreamWriter"/>
        /// for <paramref name="category"/>. Must be called while the category lock is held.
        /// </summary>
        private StreamWriter GetOrCreateWriter(string category)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            switch (category)
            {
                case "ai":
                    if (_aiDate != today) { _aiWriter?.Dispose(); _aiWriter = null; _aiDate = today; }
                    return _aiWriter   ??= OpenWriter("ai",       today);
                case "wiki":
                    if (_wikiDate != today) { _wikiWriter?.Dispose(); _wikiWriter = null; _wikiDate = today; }
                    return _wikiWriter ??= OpenWriter("wiki",     today);
                default:
                    if (_terminalDate != today) { _terminalWriter?.Dispose(); _terminalWriter = null; _terminalDate = today; }
                    return _terminalWriter ??= OpenWriter("terminal", today);
            }
        }

        private StreamWriter OpenWriter(string category, string date)
            => new(LogFilePath(category, date), append: true, System.Text.Encoding.UTF8) { AutoFlush = true };

        public void Dispose()
        {
            lock (_terminalLock) { _terminalWriter?.Dispose(); _terminalWriter = null; }
            lock (_aiLock)       { _aiWriter?.Dispose();       _aiWriter       = null; }
            lock (_wikiLock)     { _wikiWriter?.Dispose();     _wikiWriter     = null; }
            _disposed = true;
        }
    }
}
