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
            // Look for config directory relative to the executable
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string configDir = Path.Combine(exeDir, "config");
            if (Directory.Exists(configDir))
                return configDir;
            // Fallback: user appdata
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PowerTerminal", "config");
        }

        private void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(_baseDir);
            Directory.CreateDirectory(Path.Combine(_baseDir, "wikis"));
            Directory.CreateDirectory(Path.Combine(_baseDir, "..", "logs"));
        }

        public string BaseDir => _baseDir;
        public string WikiDir => Path.Combine(_baseDir, "wikis");
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
