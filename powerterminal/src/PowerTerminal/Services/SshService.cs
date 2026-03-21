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
using Terminal.Ssh;

namespace PowerTerminal.Services
{
    /// <summary>
    /// Manages SSH connections and shell channels.
    /// Implements <see cref="ISshTerminalSession"/> so it can be attached directly to
    /// <c>Terminal.Controls.TerminalControl</c>.
    /// </summary>
    public class SshService : ISshTerminalSession
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

        // ── ISshTerminalSession events ───────────────────────────────────────

        /// <summary>Raised whenever raw bytes arrive from the remote shell.</summary>
        public event EventHandler<byte[]>? DataReceived;

        /// <summary>Raised when the SSH session encounters a non-fatal error.</summary>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>Raised when the connection is lost (cleanly or with an error).</summary>
        public event EventHandler? Disconnected;

        /// <summary>
        /// Optional callback to write status text directly into the terminal
        /// (e.g. "Connection failed: ..."). Called from the SSH background thread.
        /// </summary>
        public Action<string>? LocalWrite { get; set; }

        /// <summary>
        /// Global folder to search for SSH private key files (id_rsa, id_ed25519, …).
        /// </summary>
        public string SshKeysFolder { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        // ── ISshTerminalSession.ConnectAsync ─────────────────────────────────

        public async Task ConnectAsync(
            string host, int port, string username,
            Func<string, CancellationToken, Task<string>> promptForPassword,
            int columns, int rows,
            CancellationToken ct = default)
        {
            Disconnect();

            var connection = new SshConnection
            {
                Host = host, Port = port, Username = username,
                Name = $"{username}@{host}"
            };
            CurrentConnection = connection;
            _sessionName = connection.Name;
            _log.LogTerminalEvent(_sessionName, $"Connecting to {username}@{host}:{port}");

            await Task.Run(async () =>
            {
                // Phase A: try private key (once, no user interaction)
                var keyFile = FindPrivateKeyForHost(host, username);
                PrivateKeyAuthenticationMethod? pkAuth = null;
                if (keyFile != null)
                {
                    try
                    {
                        _log.LogTerminalEvent(_sessionName, $"Using private key: {keyFile}");
                        pkAuth = new PrivateKeyAuthenticationMethod(username,
                            new PrivateKeyFile(keyFile));
                    }
                    catch (Exception ex)
                    {
                        _log.LogTerminalEvent(_sessionName,
                            $"Could not load key '{keyFile}': {ex.Message}. Falling back to password.");
                    }
                }

                if (pkAuth != null)
                {
                    var connInfo = new ConnectionInfo(host, port, username, pkAuth);
                    _client = new SshClient(connInfo);
                    _client.ErrorOccurred += (_, e) =>
                    {
                        _log.LogTerminalEvent(_sessionName, $"SSH error: {e.Exception?.Message}");
                        ErrorOccurred?.Invoke(this, e.Exception?.Message ?? "Unknown error");
                        Disconnected?.Invoke(this, EventArgs.Empty);
                    };

                    try
                    {
                        _client.Connect();
                        goto Connected;
                    }
                    catch (SshAuthenticationException)
                    {
                        _client.Dispose();
                        _client = null;
                    }
                }

                // Phase B: password with up to 3 attempts
                const int MaxAttempts = 3;
                string? deniedPrefix = null;
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    string promptText = (deniedPrefix != null ? deniedPrefix + "\r\n" : "")
                                        + $"{username}@{host.ToLowerInvariant()}'s password: ";
                    string password = await promptForPassword(promptText, ct);
                    if (string.IsNullOrEmpty(password))
                        throw new OperationCanceledException("Password entry cancelled.");

                    var pwdAuth  = new PasswordAuthenticationMethod(username, password);
                    var kbAuth   = new KeyboardInteractiveAuthenticationMethod(username);
                    kbAuth.AuthenticationPrompt += (_, args) =>
                    {
                        foreach (var p in args.Prompts) p.Response = password;
                    };

                    var connInfo = new ConnectionInfo(host, port, username, pwdAuth, kbAuth)
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };

                    try { _client?.Disconnect(); } catch { }
                    try { _client?.Dispose();    } catch { }
                    _client = new SshClient(connInfo);
                    _client.ErrorOccurred += (_, e) =>
                    {
                        _log.LogTerminalEvent(_sessionName, $"SSH error: {e.Exception?.Message}");
                        ErrorOccurred?.Invoke(this, e.Exception?.Message ?? "Unknown error");
                        Disconnected?.Invoke(this, EventArgs.Empty);
                    };

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                    try
                    {
                        await Task.Run(() => _client.Connect(), linked.Token);
                        if (_client.IsConnected) break;
                        deniedPrefix = "Permission denied, please try again.";
                    }
                    catch (SshAuthenticationException)
                    {
                        deniedPrefix = "Permission denied, please try again.";
                        if (attempt == MaxAttempts)
                            throw new SshAuthenticationException("Too many authentication failures.");
                    }
                }

                Connected:
                var termModes = new Dictionary<TerminalModes, uint>
                {
                    [TerminalModes.ECHO]  = 1,
                    [TerminalModes.ICRNL] = 1,
                    [TerminalModes.ONLCR] = 1,
                };
                _shellStream = (_client ?? throw new InvalidOperationException("SSH client was not initialized."))
                    .CreateShellStream("xterm-256color", (uint)columns, (uint)rows, 0, 0, 65536, termModes);
            }, ct);

            _log.LogTerminalEvent(_sessionName, "Connected");
            StartReading();
            await GatherMachineInfoAsync();
        }

        // ── ISshTerminalSession send methods ─────────────────────────────────

        public void Send(byte[] data)
        {
            if (_shellStream == null || !IsConnected) return;
            try
            {
                _shellStream.Write(data, 0, data.Length);
                _shellStream.Flush();
                var logContent = Encoding.UTF8.GetString(data)
                    .Replace("\r\n", "\\n").Replace("\n", "\\n");
                _log.LogTerminalInput(_sessionName, logContent);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { ErrorOccurred?.Invoke(this, ex.Message); }
        }

        public void Send(byte b) => Send(new[] { b });

        public void Send(string text) => Send(Encoding.UTF8.GetBytes(text));

        /// <summary>Legacy string-based send (kept for internal use).</summary>
        public void SendData(string data) => Send(data);

        public void SendCursorPositionReport(int row, int col)
            => Send($"\x1b[{row + 1};{col + 1}R");

        // ── ISshTerminalSession resize ────────────────────────────────────────

        public void Resize(int columns, int rows)
        {
            if (_shellStream == null) return;
            try
            {
                // Try ResizeTerminal method (SSH.NET 2025.1.0)
                var method = _shellStream.GetType().GetMethod("ResizeTerminal",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(_shellStream, new object[] { (uint)columns, (uint)rows, 0u, 0u });
                    return;
                }
                // Fall back to internal channel field
                var channelField = _shellStream.GetType()
                    .GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (channelField?.GetValue(_shellStream) is { } channel)
                {
                    var changeMethod = channel.GetType().GetMethod("SendWindowChangeRequest",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    changeMethod?.Invoke(channel, new object[] { (uint)columns, (uint)rows, 0u, 0u });
                }
            }
            catch { /* resize is best-effort */ }
        }

        // ── Background read loop ─────────────────────────────────────────────

        private void StartReading()
        {
            _readCts = new CancellationTokenSource();
            var token = _readCts.Token;
            Task.Run(async () =>
            {
                var buffer = new byte[65536];
                var stream = _shellStream;
                while (!token.IsCancellationRequested && stream != null)
                {
                    try
                    {
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (read <= 0) break;

                        var data = new byte[read];
                        Array.Copy(buffer, data, read);

                        var logContent = Encoding.UTF8.GetString(data, 0, read)
                            .Replace("\r\n", "\\n").Replace("\n", "\\n");
                        try { _log.LogTerminalOutput(_sessionName, logContent); } catch { }

                        DataReceived?.Invoke(this, data);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                        {
                            ErrorOccurred?.Invoke(this, ex.Message);
                            Disconnected?.Invoke(this, EventArgs.Empty);
                        }
                        break;
                    }
                }

                if (!token.IsCancellationRequested)
                    Disconnected?.Invoke(this, EventArgs.Empty);
            }, token);
        }

        // ── Machine info ─────────────────────────────────────────────────────

        private async Task GatherMachineInfoAsync()
        {
            if (_client == null || !IsConnected) return;
            MachineInfo = new MachineInfo();
            await Task.Run(() =>
            {
                MachineInfo.Hostname         = RunCommand("hostname").Trim();
                MachineInfo.OperatingSystem  = RunCommand("uname -o 2>/dev/null || uname -s").Trim();
                MachineInfo.OsVersion        = RunCommand("cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d= -f2 | tr -d '\"'").Trim();
                MachineInfo.KernelVersion    = RunCommand("uname -r").Trim();
                MachineInfo.HomeFolder       = RunCommand("echo $HOME").Trim();
                MachineInfo.CurrentDirectory = RunCommand("pwd").Trim();
                MachineInfo.Hardware         = RunCommand("uname -m").Trim();
                MachineInfo.CpuInfo          = RunCommand("grep 'model name' /proc/cpuinfo 2>/dev/null | head -1 | cut -d: -f2").Trim();
                MachineInfo.TotalMemory      = RunCommand("free -h 2>/dev/null | awk '/^Mem:/{print $2}'").Trim();
                MachineInfo.DiskSizes        = RunCommand("df -h --total 2>/dev/null | tail -1 | awk '{print $2}'").Trim();
                MachineInfo.IpAddress        = RunCommand("hostname -I 2>/dev/null | awk '{print $1}'").Trim();
                MachineInfo.Uptime           = RunCommand("uptime -p 2>/dev/null || uptime").Trim();
                MachineInfo.Username         = RunCommand("whoami").Trim();
                MachineInfo.LastUpdated      = DateTime.Now;
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
            catch { return string.Empty; }
        }

        // ── Key discovery ────────────────────────────────────────────────────

        private string? FindPrivateKeyForHost(string host, string username)
        {
            if (!Directory.Exists(SshKeysFolder)) return null;

            static bool IsPrivateKey(string path)
            {
                if (!File.Exists(path)) return false;
                try
                {
                    using var sr = new StreamReader(path);
                    return (sr.ReadLine() ?? string.Empty).StartsWith("-----BEGIN", StringComparison.Ordinal);
                }
                catch { return false; }
            }

            var byHost = Path.Combine(SshKeysFolder, host);
            if (IsPrivateKey(byHost)) return byHost;

            var knownHosts = Path.Combine(SshKeysFolder, "known_hosts");
            if (File.Exists(knownHosts))
            {
                foreach (var line in File.ReadLines(knownHosts))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                    var parts = line.Split(' ', 2);
                    if (parts.Length < 1) continue;
                    if (parts[0].Split(',').Any(h =>
                            h.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                            h.Equals($"[{host}]", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var name in new[] { "id_ed25519", "id_rsa", "id_ecdsa", "id_dsa" })
                        {
                            var candidate = Path.Combine(SshKeysFolder, name);
                            if (IsPrivateKey(candidate)) return candidate;
                        }
                        break;
                    }
                }
            }

            foreach (var name in new[] { "id_ed25519", "id_rsa", "id_ecdsa", "id_dsa" })
            {
                var candidate = Path.Combine(SshKeysFolder, name);
                if (IsPrivateKey(candidate)) return candidate;
            }

            return null;
        }

        // ── Disconnect / Dispose ─────────────────────────────────────────────

        public void Disconnect()
        {
            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = null;
            try { _shellStream?.Dispose(); } catch { }
            _shellStream = null;
            if (_client?.IsConnected == true)
            {
                try { _client.Disconnect(); } catch { }
                _log.LogTerminalEvent(_sessionName, "Disconnected");
            }
            try { _client?.Dispose(); } catch { }
            _client = null;
            CurrentConnection = null;
            MachineInfo = null;
        }

        public void Dispose() => Disconnect();
    }
}