using System;
using System.IO;

namespace PowerTerminal.Services
{
    /// <summary>Provides separate log files for terminal, AI, and wiki interactions.</summary>
    public class LoggingService
    {
        private readonly string _logDir;
        private readonly object _terminalLock = new();
        private readonly object _aiLock = new();
        private readonly object _wikiLock = new();

        public LoggingService(string logDir)
        {
            _logDir = logDir;
            Directory.CreateDirectory(logDir);
        }

        // ── Terminal logging ──────────────────────────────────────────────────────

        public void LogTerminalInput(string sessionName, string data)
            => WriteLog("terminal", $"[{Ts()}] [{sessionName}] INPUT: {data}");

        public void LogTerminalOutput(string sessionName, string data)
            => WriteLog("terminal", $"[{Ts()}] [{sessionName}] OUTPUT: {data}");

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

        private string LogFilePath(string category)
        {
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            return Path.Combine(_logDir, $"{category}_{date}.log");
        }

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
                try
                {
                    File.AppendAllText(LogFilePath(category), line + Environment.NewLine);
                }
                catch
                {
                    // ignore logging failures
                }
            }
        }
    }
}
