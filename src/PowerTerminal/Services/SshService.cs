using System;
using System.Collections.Generic;
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
        /// Must be set before <see cref="ConnectAsync"/> is called when no private key is configured;
        /// if left null the keyboard-interactive method is not added and connection will fail on
        /// password-protected servers.
        /// </summary>
        public Func<string, string>? PasswordPrompt { get; set; }

        public async Task ConnectAsync(SshConnection connection)
        {
            Disconnect();
            CurrentConnection = connection;
            _sessionName = connection.Name;
            _log.LogTerminalEvent(_sessionName, $"Connecting to {connection.Username}@{connection.Host}:{connection.Port}");

            await Task.Run(() =>
            {
                var authMethods = new List<AuthenticationMethod>();

                if (!string.IsNullOrWhiteSpace(connection.PrivateKeyPath))
                {
                    authMethods.Add(new PrivateKeyAuthenticationMethod(
                        connection.Username,
                        new PrivateKeyFile(connection.PrivateKeyPath)));
                }
                else if (PasswordPrompt != null)
                {
                    // No private key: use keyboard-interactive so the server can prompt for a password.
                    var kia = new KeyboardInteractiveAuthenticationMethod(connection.Username);
                    kia.AuthenticationPrompt += (_, e) =>
                    {
                        foreach (var prompt in e.Prompts)
                            prompt.Response = PasswordPrompt(prompt.Request.TrimEnd());
                    };
                    authMethods.Add(kia);
                }
                else
                {
                    // No key and no prompt handler: fall back to none-auth for key-free servers.
                    authMethods.Add(new NoneAuthenticationMethod(connection.Username));
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
                _client.Connect();

                _shellStream = _client.CreateShellStream(
                    "xterm-256color", 220, 50, 1760, 400, 4096);
            });

            _log.LogTerminalEvent(_sessionName, "Connected");
            StartReading();
            await GatherMachineInfoAsync();
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
            // Access the underlying channel via reflection to send window change request.
            // IChannelSession is internal in SSH.NET so we use dynamic dispatch.
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
            catch
            {
                // Resize not critical – ignore if unavailable
            }
        }

        private void StartReading()
        {
            _readCts = new CancellationTokenSource();
            var token = _readCts.Token;
            Task.Run(() =>
            {
                var buffer = new byte[4096];
                while (!token.IsCancellationRequested && IsConnected && _shellStream != null)
                {
                    try
                    {
                        if (_shellStream.DataAvailable)
                        {
                            int read = _shellStream.Read(buffer, 0, buffer.Length);
                            if (read > 0)
                            {
                                string data = Encoding.UTF8.GetString(buffer, 0, read);
                                _log.LogTerminalOutput(_sessionName, data.Replace("\r\n", "\\n").Replace("\n", "\\n"));
                                DataReceived?.Invoke(data);
                            }
                        }
                        else
                        {
                            Thread.Sleep(10);
                        }
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
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
