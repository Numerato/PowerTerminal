using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Directory.CreateDirectory(Path.Combine(_baseDir, "commands"));
            string logDir = Path.GetFullPath(Path.Combine(_baseDir, "..", "logs"));
            Directory.CreateDirectory(logDir);
            WriteStartupDiagnostic(logDir);
            EnsureDefaultPersonalPack();
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

        public string BaseDir    => _baseDir;
        public string WikiDir    => Path.Combine(_baseDir, "wikis");
        public string CommandDir => Path.Combine(_baseDir, "commands");
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

        // ── Custom Variables ─────────────────────────────────────────────────

        public List<CustomVariable> LoadCustomVariables()
        {
            string path = Path.Combine(_baseDir, "variables.json");
            if (!File.Exists(path))
                return new List<CustomVariable>();
            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<CustomVariable>>(json, JsonOptions)
                       ?? new List<CustomVariable>();
            }
            catch
            {
                return new List<CustomVariable>();
            }
        }

        public void SaveCustomVariables(IEnumerable<CustomVariable> variables)
        {
            string path = Path.Combine(_baseDir, "variables.json");
            File.WriteAllText(path, JsonSerializer.Serialize(variables, JsonOptions));
        }

        // ── Command Packs ─────────────────────────────────────────────────────

        /// <summary>
        /// Loads all command packs from the commands/ directory.
        /// Each *.json file is an array of <see cref="LinuxCommand"/>.
        /// </summary>
        public List<LinuxCommand> LoadAllCommands()
        {
            var result = new List<LinuxCommand>();
            string dir = CommandDir;
            if (!Directory.Exists(dir))
                return result;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var pack = JsonSerializer.Deserialize<List<LinuxCommand>>(json, JsonOptions);
                    if (pack != null)
                        result.AddRange(pack);
                }
                catch
                {
                    // skip malformed pack files
                }
            }
            return result;
        }

        /// <summary>
        /// Loads all command packs individually, preserving which commands belong to which file.
        /// Returns a list of (fileName, commands) tuples, sorted by filename.
        /// </summary>
        public List<(string FileName, List<LinuxCommand> Commands)> LoadCommandPacks()
        {
            var result = new List<(string, List<LinuxCommand>)>();
            string dir = CommandDir;
            if (!Directory.Exists(dir))
                return result;
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var commands = JsonSerializer.Deserialize<List<LinuxCommand>>(json, JsonOptions)
                                   ?? new List<LinuxCommand>();
                    result.Add((Path.GetFileName(file), commands));
                }
                catch
                {
                    // skip malformed pack files; still show them as empty
                    result.Add((Path.GetFileName(file), new List<LinuxCommand>()));
                }
            }
            return result;
        }

        /// <summary>Overwrites a single pack file with the given commands.</summary>
        public void SaveCommandPack(string fileName, IEnumerable<LinuxCommand> commands)
        {
            string path = Path.Combine(CommandDir, fileName);
            File.WriteAllText(path, JsonSerializer.Serialize(commands, JsonOptions));
        }

        /// <summary>
        /// Creates a new empty pack file.
        /// <paramref name="displayName"/> is sanitized to a safe filename.
        /// Returns the final filename (e.g. "my_pack.json").
        /// </summary>
        public string CreateCommandPack(string displayName)
        {
            string safe = SanitizeFileName(displayName);
            if (string.IsNullOrWhiteSpace(safe)) safe = "pack";
            string fileName = safe + ".json";
            string path     = Path.Combine(CommandDir, fileName);
            // Avoid clobbering an existing file
            int n = 2;
            while (File.Exists(path))
            {
                fileName = $"{safe}_{n++}.json";
                path     = Path.Combine(CommandDir, fileName);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(new List<LinuxCommand>(), JsonOptions));
            return fileName;
        }

        /// <summary>Deletes a pack file by filename. Silently ignores missing files.</summary>
        public void DeleteCommandPack(string fileName)
        {
            string path = Path.Combine(CommandDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Creates <c>personal.json</c> in the commands/ directory if it does not already exist.
        /// Called during startup so there is always a writable pack for user-created commands.
        /// </summary>
        private void EnsureDefaultPersonalPack()
        {
            string path = Path.Combine(CommandDir, "personal.json");
            if (!File.Exists(path))
                File.WriteAllText(path, JsonSerializer.Serialize(new List<LinuxCommand>(), JsonOptions));
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
