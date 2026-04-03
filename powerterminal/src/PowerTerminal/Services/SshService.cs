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

        // Output is held until all initialization is complete so the terminal
        // renders once cleanly instead of flashing during startup.
        private readonly List<byte[]> _heldOutput = new();
        private volatile bool _holdOutput = false;

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

                bool connected = false;

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
                        connected = true;
                    }
                    catch (SshAuthenticationException)
                    {
                        _client.Dispose();
                        _client = null;
                    }
                }

                // Phase B: password with up to 3 attempts (skipped when key auth succeeded)
                if (!connected)
                {
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
                }

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

            // Hold terminal output while initialization runs so the terminal
            // renders once cleanly (no flashing prompt during startup).
            _holdOutput = true;
            StartReading();

            // Run both init tasks in parallel; inject no longer needs a head-start delay.
            await Task.WhenAll(GatherMachineInfoAsync(), InjectShellDefinitionsAsync());

            // Release all buffered output in one shot → single clean render.
            _holdOutput = false;
            byte[][] pending;
            lock (_heldOutput)
            {
                pending = _heldOutput.ToArray();
                _heldOutput.Clear();
            }
            foreach (var chunk in pending)
                DataReceived?.Invoke(this, chunk);
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

        /// <summary>Legacy alias for Send(string) — kept for internal callers only.</summary>
        internal void SendData(string data) => Send(data);

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

                        if (_holdOutput)
                        {
                            lock (_heldOutput) { _heldOutput.Add(data); }
                        }
                        else
                        {
                            DataReceived?.Invoke(this, data);
                        }
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
            var info = new MachineInfo();

            // Run all probe commands in parallel — SSH multiplexes channels fine.
            // Each task writes to a distinct property so no locking is needed.
            await Task.WhenAll(
                Task.Run(() => { info.Hostname        = RunCommand("hostname").Trim(); }),
                Task.Run(() => { info.OperatingSystem = RunCommand("uname -o 2>/dev/null || uname -s").Trim(); }),
                Task.Run(() => { info.OsVersion       = RunCommand("cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d= -f2 | tr -d '\"'").Trim(); }),
                Task.Run(() => { info.KernelVersion   = RunCommand("uname -r").Trim(); }),
                Task.Run(() => { info.HomeFolder      = RunCommand("echo $HOME").Trim(); }),
                Task.Run(() => { info.CurrentDirectory = RunCommand("pwd").Trim(); }),
                Task.Run(() => { info.Hardware        = RunCommand("uname -m").Trim(); }),
                Task.Run(() => { info.CpuInfo         = RunCommand("grep 'model name' /proc/cpuinfo 2>/dev/null | head -1 | cut -d: -f2").Trim(); }),
                Task.Run(() => { info.TotalMemory     = RunCommand("free -h 2>/dev/null | awk '/^Mem:/{print $2}'").Trim(); }),
                Task.Run(() => { info.DiskSizes       = RunCommand("df -h --total 2>/dev/null | tail -1 | awk '{print $2}'").Trim(); }),
                Task.Run(() => { info.IpAddress       = RunCommand("hostname -I 2>/dev/null | awk '{print $1}'").Trim(); }),
                Task.Run(() => { info.Username        = RunCommand("whoami").Trim(); }),
                Task.Run(() => { info.DefaultShell    = RunCommand("echo $SHELL").Trim(); }),
                Task.Run(() => { info.Timezone        = RunCommand("cat /etc/timezone 2>/dev/null || timedatectl show -p Timezone --value 2>/dev/null || date +%Z").Trim(); }),
                Task.Run(() => { info.CpuCount        = RunCommand("nproc 2>/dev/null || grep -c ^processor /proc/cpuinfo").Trim(); }),
                Task.Run(() => { info.FreeMemory      = RunCommand("free -h 2>/dev/null | awk '/^Mem:/{print $4}'").Trim(); }),
                Task.Run(() => { info.FreeDisk        = RunCommand("df -h / 2>/dev/null | tail -1 | awk '{print $4}'").Trim(); }),
                Task.Run(() => { info.PublicIp        = RunCommand("curl -s --max-time 5 ifconfig.me 2>/dev/null || curl -s --max-time 5 api.ipify.org 2>/dev/null").Trim(); }),
                Task.Run(() => { info.SudoUser        = RunCommand("logname 2>/dev/null || echo ${SUDO_USER:-$USER}").Trim(); })
            );

            info.LastUpdated = DateTime.Now;
            MachineInfo = info;
        }

        public async Task RefreshMachineInfoAsync()
        {
            if (_client == null || !IsConnected) return;
            var info = MachineInfo ?? new MachineInfo();
            await Task.WhenAll(
                Task.Run(() => { info.CurrentDirectory = RunCommand("pwd").Trim(); }),
                Task.Run(() => { info.DiskSizes        = RunCommand("df -h --total 2>/dev/null | tail -1 | awk '{print $2}'").Trim(); })
            );
            info.LastUpdated = DateTime.Now;
            MachineInfo = info;
        }

        /// <summary>
        /// Ensures a real 'pwe' executable exists in the user's bin directory
        /// (~/bin or ~/.local/bin).  Because bash scans PATH for completion candidates,
        /// this makes 'p'+Tab list 'pwe'.
        ///
        /// If the bin dir didn't exist before this session (Ubuntu's .profile only adds
        /// ~/bin to PATH when the dir exists at login time), we also inject
        /// "export PATH=..." into the interactive shell stream.  Since _holdOutput is
        /// still true at that point, the echo lands in the held buffer and is rendered
        /// as part of the normal initial login output — no flash, no history entry.
        /// All subsequent sessions have ~/bin in PATH automatically.
        /// </summary>
        private async Task InjectShellDefinitionsAsync()
        {
            if (!IsConnected) return;

            // GatherMachineInfoAsync runs in parallel, so query $HOME directly.
            string homeDir = await Task.Run(() => RunCommand("echo $HOME").Trim());
            if (string.IsNullOrEmpty(homeDir)) return;

            // Exec channels don't source .bashrc/.profile, so check directory existence
            // rather than $PATH.  If the dir existed before this session it is already
            // in the interactive shell's PATH (via .profile); if not, we add it below.
            (string binDir, bool existedBefore) = await Task.Run(() =>
            {
                foreach (var candidate in new[] { $"{homeDir}/bin", $"{homeDir}/.local/bin" })
                {
                    string result = RunCommand($"[ -d \"{candidate}\" ] && echo yes || echo no").Trim();
                    if (result == "yes") return (candidate, true);
                }
                return ($"{homeDir}/bin", false);  // will be created
            });

            await Task.Run(() =>
            {
                // Always (re)write to ensure the script is the silent version —
                // PowerTerminal intercepts the command client-side; the script only
                // exists so bash can find it for tab-completion.
                RunCommand($"mkdir -p \"{binDir}\"");
                RunCommand(
                    $"printf '#!/usr/bin/env bash\\nexit 0\\n'" +
                    $" > \"{binDir}/pwe\"");
                RunCommand($"chmod +x \"{binDir}/pwe\"");
            });

            // If ~/bin was just created it is not yet in the interactive session's PATH.
            // Send a PATH update to the shell stream now.  _holdOutput is still true so
            // the echo is buffered and released with the rest of the login output —
            // it appears once in the terminal on this first-ever session, then never again.
            if (!existedBefore && _shellStream != null)
            {
                Send($"export PATH=\"{binDir}:$PATH\"\r");
            }
        }


        public string RunCommand(string cmd)
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

        public Task<string> RunCommandAsync(string cmd) => Task.Run(() => RunCommand(cmd));

        public Task<(string Output, int ExitCode)> RunCommandWithResultAsync(string cmd)
            => Task.Run(() =>
            {
                if (_client == null || !IsConnected) return (string.Empty, -1);
                try
                {
                    using var command = _client.CreateCommand(cmd);
                    command.CommandTimeout = TimeSpan.FromSeconds(30);
                    string output = command.Execute() ?? string.Empty;
                    return (output, command.ExitStatus ?? -1);
                }
                catch (Exception ex) { return ($"Error: {ex.Message}", -1); }
            });

        /// <summary>
        /// Reads a remote file's content. Supports sudo (password passed via stdin).
        /// </summary>
        public Task<(string Content, bool Success)> ReadFileAsync(string path, string sudoPassword = "")
            => Task.Run(async () =>
            {
                string esc = EscapePath(path);
                string cmd = string.IsNullOrEmpty(sudoPassword)
                    ? $"cat -- \"{esc}\""
                    : $"echo '{EscapeShellSingle(sudoPassword)}' | sudo -S cat -- \"{esc}\" 2>/dev/null";

                var (output, exit) = await RunCommandWithResultAsync(cmd);
                return (output, exit == 0);
            });

        /// <summary>
        /// Writes content to a remote file. For sudo writes: writes to a temp file first,
        /// then uses sudo to copy it to the destination.
        /// </summary>
        public async Task<bool> WriteFileAsync(string path, string content, string sudoPassword = "")
        {
            string esc = EscapePath(path);
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));

            if (string.IsNullOrEmpty(sudoPassword))
            {
                string cmd = $"echo '{b64}' | base64 -d > \"{esc}\"";
                var (_, exit) = await RunCommandWithResultAsync(cmd);
                return exit == 0;
            }
            else
            {
                // Write to temp file (no sudo needed for /tmp), then sudo-copy to destination.
                string tmp  = $"/tmp/.pe_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                string pass = EscapeShellSingle(sudoPassword);

                string step1 = $"echo '{b64}' | base64 -d > \"{tmp}\"";
                var (_, e1) = await RunCommandWithResultAsync(step1);
                if (e1 != 0) return false;

                string step2 = $"echo '{pass}' | sudo -S cp \"{tmp}\" \"{esc}\" 2>&1 && rm -f \"{tmp}\"";
                var (out2, e2) = await RunCommandWithResultAsync(step2);
                if (e2 != 0)
                {
                    await RunCommandWithResultAsync($"rm -f \"{tmp}\"");
                    return false;
                }
                return true;
            }
        }

        private static string EscapePath(string path)
            => path.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string EscapeShellSingle(string s)
            => s.Replace("'", "'\\''");

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

            // 1. Key file named after the host (e.g. ~/.ssh/myserver)
            var byHost = Path.Combine(SshKeysFolder, host);
            if (IsPrivateKey(byHost)) return byHost;

            // 2. Standard key filenames in preference order
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
            // Capture first so a concurrent Disconnect() can't race between
            // Cancel() and Dispose() after _readCts is set to null.
            var cts = _readCts;
            _readCts = null;
            cts?.Cancel();
            cts?.Dispose();
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
