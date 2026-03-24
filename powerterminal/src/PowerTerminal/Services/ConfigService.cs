using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerTerminal.Models;

namespace PowerTerminal.Services
{
    /// <summary>Loads and saves all JSON configuration files from the /config directory.</summary>
    public class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _baseDir;

        public ConfigService(string? baseDir = null)
        {
            _baseDir = baseDir ?? GetDefaultConfigDir();
            EnsureDirectoriesExist();
        }

        private static string GetDefaultConfigDir()
        {
            // Always store user data in %APPDATA%\PowerTerminal\config so the
            // location is consistent regardless of where the exe lives (taskbar
            // pin, publish directory, Debug build, etc.).
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PowerTerminal", "config");

            // Migration: copy any files from the exe-local config directory that
            // are NOT yet present in AppData. Runs every startup so files added
            // while running from the dev build are picked up automatically.
            string exeConfigDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            if (Directory.Exists(exeConfigDir))
                MigrateConfigDir(exeConfigDir, appDataDir);

            return appDataDir;
        }

        /// <summary>
        /// Copies files from <paramref name="source"/> to <paramref name="dest"/> that are
        /// either absent in the destination or older than the source. Silently ignores errors.
        /// </summary>
        private static void MigrateConfigDir(string source, string dest)
        {
            try
            {
                foreach (string srcFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(source, srcFile);
                    string destFile = Path.Combine(dest, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                    // Only overwrite if the source is newer — preserves edits made in AppData.
                    if (!File.Exists(destFile) ||
                        File.GetLastWriteTimeUtc(srcFile) > File.GetLastWriteTimeUtc(destFile))
                    {
                        File.Copy(srcFile, destFile, overwrite: true);
                    }
                }
            }
            catch { /* migration is best-effort; don't crash startup */ }
        }

        private void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(_baseDir);
            Directory.CreateDirectory(Path.Combine(_baseDir, "wikis"));
            string logDir = Path.GetFullPath(Path.Combine(_baseDir, "..", "logs"));
            Directory.CreateDirectory(logDir);
            WriteStartupDiagnostic(logDir);
        }

        /// <summary>Always writes a brief startup entry to help diagnose config-path issues.</summary>
        private void WriteStartupDiagnostic(string logDir)
        {
            try
            {
                string connPath = Path.Combine(_baseDir, "connections.json");
                bool   exists   = File.Exists(connPath);
                int    count    = exists ? (LoadConnections().Count) : -1;
                string diagFile = Path.Combine(logDir, "startup.log");
                string entry    = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BaseDir={_baseDir} | Connections={count} | Exe={AppDomain.CurrentDomain.BaseDirectory}";
                File.AppendAllText(diagFile, entry + Environment.NewLine);
            }
            catch { /* diagnostic writes must never crash the app */ }
        }

        public string BaseDir => _baseDir;
        public string WikiDir => Path.Combine(_baseDir, "wikis");
        // Logs live next to config, under %APPDATA%\PowerTerminal\logs
        public string LogDir => Path.GetFullPath(Path.Combine(_baseDir, "..", "logs"));

        // ── Connections ──────────────────────────────────────────────────────────

        public List<SshConnection> LoadConnections()
        {
            string path = Path.Combine(_baseDir, "connections.json");
            if (!File.Exists(path))
                return new List<SshConnection>();
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<SshConnection>>(json, JsonOptions)
                       ?? new List<SshConnection>();
            }
            catch
            {
                // Return empty list on corrupt file rather than crashing startup.
                return new List<SshConnection>();
            }
        }

        public void SaveConnections(IEnumerable<SshConnection> connections)
        {
            string path = Path.Combine(_baseDir, "connections.json");
            File.WriteAllText(path, JsonSerializer.Serialize(connections, JsonOptions));
        }

        // ── Settings ─────────────────────────────────────────────────────────────

        public AppSettings LoadSettings()
        {
            string path = Path.Combine(_baseDir, "settings.json");
            if (!File.Exists(path))
                return new AppSettings();
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                       ?? new AppSettings();
            }
            catch
            {
                // Return defaults on corrupt file rather than crashing startup.
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            string path = Path.Combine(_baseDir, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
        }

        // ── Wiki ─────────────────────────────────────────────────────────────────

        public List<WikiEntry> LoadAllWikis()
        {
            var result = new List<WikiEntry>();
            string dir = WikiDir;
            if (!Directory.Exists(dir))
                return result;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var entry = JsonSerializer.Deserialize<WikiEntry>(json, JsonOptions);
                    if (entry != null)
                    {
                        entry.FileName = Path.GetFileName(file);
                        result.Add(entry);
                    }
                }
                catch
                {
                    // skip malformed files
                }
            }
            return result;
        }

        public void SaveWiki(WikiEntry wiki)
        {
            bool isNew = string.IsNullOrWhiteSpace(wiki.FileName);
            if (isNew)
            {
                string safeName = SanitizeFileName(wiki.Title);
                // Append 8 hex chars of the entry GUID so identically-titled wikis
                // never overwrite each other.
                string suffix = wiki.Id.ToString("N")[..8];
                wiki.FileName  = $"{safeName}_{suffix}.json";
                wiki.CreatedAt = DateTime.UtcNow;
            }
            wiki.UpdatedAt = DateTime.UtcNow;
            string path = Path.Combine(WikiDir, wiki.FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(wiki, JsonOptions));
        }

        public void DeleteWiki(WikiEntry wiki)
        {
            if (!string.IsNullOrWhiteSpace(wiki.FileName))
            {
                string path = Path.Combine(WikiDir, wiki.FileName);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Replace(' ', '_').ToLowerInvariant();
            // Guard against titles that consist entirely of invalid chars / spaces.
            return string.IsNullOrWhiteSpace(name) ? "wiki" : name;
        }
    }
}
