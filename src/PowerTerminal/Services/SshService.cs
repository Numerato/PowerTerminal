using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PowerTerminal.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace PowerTerminal.Services
{
    /// <summary>Manages SSH connections and shell channels.</summary>
    public class SshService : IDisposable
    {
        private SshClient? _client;
        private ShellStream? _shellStream;
        private CancellationTokenSource? _readCts;
        private readonly LoggingService _log;
        private string _sessionName = string.Empty;

        public SshService(LoggingService log)
        {
            _log = log;
        }

        public bool IsConnected => _client?.IsConnected ?? false;
        public SshConnection? CurrentConnection { get; private set; }
        public MachineInfo? MachineInfo { get; private set; }

        /// <summary>Raised whenever data arrives from the remote shell.</summary>
        public event Action<string>? DataReceived;

        /// <summary>Raised when the connection is lost.</summary>
        public event Action<Exception?>? Disconnected;

        /// <summary>
        /// Optional callback invoked during keyboard-interactive authentication.
        /// Receives the prompt text (e.g. "Password: ") and returns the user's response.
        /// </summary>
        public Func<string, string>? PasswordPrompt { get; set; }

        /// <summary>
        /// Global folder to search for SSH private key files (id_rsa, id_ed25519, …).
        /// Set from <see cref="AppSettings.SshKeysFolder"/> before calling
        /// <see cref="ConnectAsync"/>.
        /// </summary>
        public string SshKeysFolder { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        public async Task ConnectAsync(SshConnection connection)
        {
            Disconnect();
            CurrentConnection = connection;
            _sessionName = connection.Name;
            _log.LogTerminalEvent(_sessionName, $"Connecting to {connection.Username}@{connection.Host}:{connection.Port}");

            await Task.Run(() =>
            {
                // ── 1. Resolve private key once (shared across retry attempts) ────────
                var keyFile = FindPrivateKeyForHost(connection.Host, connection.Username);
                PrivateKeyAuthenticationMethod? pkAuth = null;
                if (keyFile != null)
                {
                    try
                    {
                        _log.LogTerminalEvent(_sessionName, $"Using private key: {keyFile}");
                        pkAuth = new PrivateKeyAuthenticationMethod(
                            connection.Username,
                            new PrivateKeyFile(keyFile));
                    }
                    catch (Exception ex)
                    {
                        _log.LogTerminalEvent(_sessionName,
                            $"Could not load private key '{keyFile}': {ex.Message}. Falling back to password auth.");
                    }
                }

                // ── 2. Connection loop (retries apply only to password auth) ──────────
                // When no private key is available and a password collector is wired up,
                // allow up to 3 attempts, showing "Permission denied" between each.
                // Key-based auth has no retry loop (key accepted or rejected once).
                const int MaxPasswordAttempts = 3;
                bool usePasswordRetry = pkAuth == null && PasswordPrompt != null;
                int maxAttempts = usePasswordRetry ? MaxPasswordAttempts : 1;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var authMethods = new List<AuthenticationMethod>();

                    if (pkAuth != null)
                    {
                        // Key available: try key, then keyboard-interactive as fallback
                        authMethods.Add(pkAuth);
                        if (PasswordPrompt != null)
                        {
                            var kia = new KeyboardInteractiveAuthenticationMethod(connection.Username);
                            kia.AuthenticationPrompt += (_, e) =>
                            {
                                foreach (var p in e.Prompts)
                                    p.Response = PasswordPrompt(p.Request);
                            };
                            authMethods.Add(kia);
                        }
                    }
                    else if (PasswordPrompt != null)
                    {
                        // No key: prompt for password inline in the terminal.
                        // Print "Permission denied" prefix on every retry after the first.
                        string prefix = attempt > 1
                            ? "\r\nPermission denied, please try again.\r\n"
                            : string.Empty;
                        var pw = PasswordPrompt(
                            $"{prefix}Password for {connection.Username}@{connection.Host}: ");
                        if (string.IsNullOrEmpty(pw))
                            throw new OperationCanceledException("Password entry cancelled.");
                        authMethods.Add(new PasswordAuthenticationMethod(connection.Username, pw));
                    }

                    if (authMethods.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "No authentication method available: configure SSH keys in the global keys folder " +
                            "or ensure a password can be collected.");
                    }

                    var connInfo = new ConnectionInfo(
                        connection.Host,
                        connection.Port,
                        connection.Username,
                        authMethods.ToArray());

                    _client = new SshClient(connInfo);
                    _client.ErrorOccurred += (s, e) =>
                    {
                        _log.LogTerminalEvent(_sessionName, $"SSH error: {e.Exception?.Message}");
                        Disconnected?.Invoke(e.Exception);
                    };

                    try
                    {
                        _client.Connect();
                        break; // success — exit the retry loop
                    }
                    catch (SshAuthenticationException) when (usePasswordRetry && attempt < maxAttempts)
                    {
                        // Wrong password — clean up and let the loop prompt again
                        _client.Dispose();
                        _client = null;
                    }
                }

                _shellStream = (_client ?? throw new InvalidOperationException("SSH client was not initialized."))
                    .CreateShellStream("xterm-256color", 220, 50, 1760, 400, 65536);
            });

            _log.LogTerminalEvent(_sessionName, "Connected");
            StartReading();
            await GatherMachineInfoAsync();
        }

        /// <summary>
        /// Searches <see cref="SshKeysFolder"/> for a private key suitable for
        /// <paramref name="host"/>. Checks in order:
        /// <list type="number">
        ///   <item>A file named exactly after the host (e.g. <c>myserver</c>)</item>
        ///   <item>Any key listed in <c>known_hosts</c> that matches the host</item>
        ///   <item>The conventional default keys: id_ed25519, id_rsa, id_ecdsa, id_dsa</item>
        /// </list>
        /// Returns <c>null</c> if no readable key is found.
        /// </summary>
        private string? FindPrivateKeyForHost(string host, string username)
        {
            if (!Directory.Exists(SshKeysFolder)) return null;

            // Helper: test whether a candidate file looks like a readable private key.
            static bool IsPrivateKey(string path)
            {
                if (!File.Exists(path)) return false;
                try
                {
                    // A private key file starts with "-----BEGIN"
                    using var sr = new StreamReader(path);
                    var firstLine = sr.ReadLine() ?? string.Empty;
                    return firstLine.StartsWith("-----BEGIN", StringComparison.Ordinal);
                }
                catch { return false; }
            }

            // 1. File named after the host
            var byHost = Path.Combine(SshKeysFolder, host);
            if (IsPrivateKey(byHost)) return byHost;

            // 2. Walk known_hosts and return the first private key that corresponds
            //    to a known pattern (the key files themselves must exist alongside it).
            var knownHosts = Path.Combine(SshKeysFolder, "known_hosts");
            if (File.Exists(knownHosts))
            {
                foreach (var line in File.ReadLines(knownHosts))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                    var parts = line.Split(' ', 2);
                    if (parts.Length < 1) continue;
                    var hostPart = parts[0];
                    if (hostPart.Split(',').Any(h =>
                            h.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                            h.Equals($"[{host}]", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Host is known – try conventional key names
                        foreach (var name in new[] { "id_ed25519", "id_rsa", "id_ecdsa", "id_dsa" })
                        {
                            var candidate = Path.Combine(SshKeysFolder, name);
                            if (IsPrivateKey(candidate)) return candidate;
                        }
                        break;
                    }
                }
            }

            // 3. Fall back to conventional default key names
            foreach (var name in new[] { "id_ed25519", "id_rsa", "id_ecdsa", "id_dsa" })
            {
                var candidate = Path.Combine(SshKeysFolder, name);
                if (IsPrivateKey(candidate)) return candidate;
            }

            return null;
        }

        public void SendData(string data)
        {
            if (_shellStream == null || !IsConnected) return;
            _shellStream.Write(data);
            _shellStream.Flush();
            _log.LogTerminalInput(_sessionName, data.Replace("\r\n", "\\n").Replace("\n", "\\n"));
        }

        public void Resize(uint columns, uint rows)
        {
            if (_shellStream == null) return;
            try
            {
                var channelField = _shellStream.GetType()
                    .GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var channel = channelField?.GetValue(_shellStream);
                if (channel != null)
                {
                    var method = channel.GetType().GetMethod("SendWindowChangeRequest",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    method?.Invoke(channel, new object[] { columns, rows, columns * 8u, rows * 16u });
                }
            }
            catch { }
        }

        private void StartReading()
        {
            _readCts = new CancellationTokenSource();
            var token = _readCts.Token;
            Task.Run(() =>
            {
                // Use a large read buffer so a burst of output (e.g. 'apt list') never
                // stalls in SSH.NET's internal pipe and exhausts the SSH channel window.
                var buffer = new byte[65536];

                // Capture a local reference; Disconnect() nulls _shellStream concurrently.
                var stream = _shellStream;
                while (!token.IsCancellationRequested && stream != null)
                {
                    try
                    {
                        // Blocking read — returns as soon as any bytes arrive, with no
                        // polling sleep.  This keeps the SSH channel window extended and
                        // prevents server-side processes from blocking on a full write buffer.
                        int read = stream.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            string data = Encoding.UTF8.GetString(buffer, 0, read);
                            _log.LogTerminalOutput(_sessionName, data.Replace("\r\n", "\\n").Replace("\n", "\\n"));
                            DataReceived?.Invoke(data);
                        }
                        else
                        {
                            // 0-byte read means the stream was closed cleanly.
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                            Disconnected?.Invoke(ex);
                        break;
                    }
                }
            }, token);
        }

        private async Task GatherMachineInfoAsync()
        {
            if (_client == null || !IsConnected) return;
            MachineInfo = new MachineInfo();

            await Task.Run(() =>
            {
                MachineInfo.Hostname    = RunCommand("hostname").Trim();
                MachineInfo.OperatingSystem = RunCommand("uname -o 2>/dev/null || uname -s").Trim();
                MachineInfo.OsVersion   = RunCommand("cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d= -f2 | tr -d '\"'").Trim();
                MachineInfo.KernelVersion = RunCommand("uname -r").Trim();
                MachineInfo.HomeFolder  = RunCommand("echo $HOME").Trim();
                MachineInfo.CurrentDirectory = RunCommand("pwd").Trim();
                MachineInfo.Hardware    = RunCommand("uname -m").Trim();
                MachineInfo.CpuInfo     = RunCommand("grep 'model name' /proc/cpuinfo 2>/dev/null | head -1 | cut -d: -f2").Trim();
                MachineInfo.TotalMemory = RunCommand("free -h 2>/dev/null | awk '/^Mem:/{print $2}'").Trim();
                MachineInfo.DiskSizes   = RunCommand("df -h --total 2>/dev/null | tail -1 | awk '{print $2}'").Trim();
                MachineInfo.IpAddress   = RunCommand("hostname -I 2>/dev/null | awk '{print $1}'").Trim();
                MachineInfo.Uptime      = RunCommand("uptime -p 2>/dev/null || uptime").Trim();
                MachineInfo.Username    = RunCommand("whoami").Trim();
                MachineInfo.LastUpdated = DateTime.Now;
            });
        }

        public async Task RefreshMachineInfoAsync()
        {
            if (_client == null || !IsConnected) return;
            await Task.Run(() =>
            {
                if (MachineInfo == null) MachineInfo = new MachineInfo();
                MachineInfo.CurrentDirectory = RunCommand("pwd").Trim();
                MachineInfo.DiskSizes        = RunCommand("df -h --total 2>/dev/null | tail -1 | awk '{print $2}'").Trim();
                MachineInfo.LastUpdated      = DateTime.Now;
            });
        }

        private string RunCommand(string cmd)
        {
            if (_client == null || !IsConnected) return string.Empty;
            try
            {
                using var command = _client.CreateCommand(cmd);
                command.CommandTimeout = TimeSpan.FromSeconds(5);
                return command.Execute() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public void Disconnect()
        {
            _readCts?.Cancel();
            _shellStream?.Dispose();
            _shellStream = null;
            if (_client?.IsConnected == true)
            {
                _client.Disconnect();
                _log.LogTerminalEvent(_sessionName, "Disconnected");
            }
            _client?.Dispose();
            _client = null;
            CurrentConnection = null;
            MachineInfo = null;
        }

        public void Dispose() => Disconnect();
    }
}
