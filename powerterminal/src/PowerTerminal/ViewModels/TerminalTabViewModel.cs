using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;
using Terminal.Ssh;

namespace PowerTerminal.ViewModels
{
    /// <summary>ViewModel for a single SSH terminal tab.</summary>
    public class TerminalTabViewModel : ViewModelBase, IDisposable
    {
        private readonly LoggingService _log;
        private SshService? _ssh;
        private string _header = "Terminal";
        private bool _isActive;
        private bool _isConnected;
        private bool _isConnecting;
        private string _statusText = "Disconnected";
        private MachineInfo? _machineInfo;

        /// <summary>
        /// When true the tab view will call <see cref="ConnectAsync"/> as soon as it loads.
        /// Cleared immediately in the View's OnLoaded so tab-switch recreations don't reconnect.
        /// </summary>
        public bool AutoConnectOnLoad { get; set; }

        /// <summary>When true the terminal intercepts "poweredit &lt;file&gt;" commands instead of forwarding them to the shell.</summary>
        public bool EnablePowerEdit { get; set; }

        /// <summary>SSH keys folder passed to <see cref="SshService"/>.</summary>
        public string SshKeysFolder { get; set; } =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        /// <summary>Visual settings for the terminal (font, size, colors, padding).</summary>
        public ThemeSettings Theme { get; set; } = new();

        public System.Windows.Thickness TerminalPadding => new System.Windows.Thickness(Theme.Padding);

        public TerminalTabViewModel(LoggingService log)
        {
            _log = log;
            ConnectCommand = new RelayCommand(
                _ => RequestConnect?.Invoke(this, EventArgs.Empty),
                _ => !IsConnected && !IsConnecting && Connection != null);
        }

        public SshConnection? Connection { get; set; }
        public ICommand ConnectCommand { get; }

        // ── Events ───────────────────────────────────────────────────────────

        /// <summary>
        /// Raised just before <see cref="ConnectAsync"/> opens the SSH connection.
        /// The View must handle this by calling
        /// <c>terminal.EnsureEmulatorInitialized(); terminal.AttachSession(session);</c>
        /// </summary>
        public event EventHandler<ISshTerminalSession>? SessionAttachRequired;

        /// <summary>Raised when the Connect command is executed via a UI button.</summary>
        public event EventHandler? RequestConnect;

        /// <summary>Raised to write a status/error message into the terminal display.</summary>
        public event Action<string>? LocalOutput;

        /// <summary>Raised when connection state changes.</summary>
        public event Action? StateChanged;

        /// <summary>Raised when a connected session ends and the tab should close.</summary>
        public event Action? TabCloseRequested;

        // ── State properties ─────────────────────────────────────────────────

        public string Header
        {
            get => _header;
            set => Set(ref _header, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => Set(ref _isActive, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (Set(ref _isConnected, value))
                    StateChanged?.Invoke();
            }
        }

        public bool IsConnecting
        {
            get => _isConnecting;
            private set => Set(ref _isConnecting, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => Set(ref _statusText, value);
        }

        public MachineInfo? MachineInfo
        {
            get => _machineInfo;
            private set => Set(ref _machineInfo, value);
        }

        // ── Shell variables for wiki substitution ─────────────────────────────

        public string CurrentDirectory => MachineInfo?.CurrentDirectory ?? "~";
        public string OperatingSystem   => MachineInfo?.OperatingSystem  ?? string.Empty;
        public string OsVersion         => MachineInfo?.OsVersion        ?? string.Empty;
        public string HomeFolder        => MachineInfo?.HomeFolder        ?? "~";
        public string Hardware          => MachineInfo?.Hardware          ?? string.Empty;
        public string DiskSizes         => MachineInfo?.DiskSizes         ?? string.Empty;
        public string IpAddress         => MachineInfo?.IpAddress         ?? string.Empty;
        public string Hostname          => MachineInfo?.Hostname          ?? string.Empty;
        public string CpuInfo           => MachineInfo?.CpuInfo           ?? string.Empty;
        public string TotalMemory       => MachineInfo?.TotalMemory       ?? string.Empty;
        public string Username          => MachineInfo?.Username          ?? string.Empty;
        public string Uptime            => MachineInfo?.Uptime            ?? string.Empty;
        public string KernelVersion     => MachineInfo?.KernelVersion     ?? string.Empty;

        public string? LogoPath => Connection?.LogoPath;

        // ── Connection ────────────────────────────────────────────────────────

        /// <summary>
        /// Connects to the SSH server. The View must provide <paramref name="passwordPrompt"/>
        /// (typically <c>terminal.PromptForPasswordAsync</c>) and the current terminal dimensions.
        /// </summary>
        public async Task ConnectAsync(
            Func<string, CancellationToken, Task<string>> passwordPrompt,
            int cols, int rows)
        {
            if (Connection == null) return;

            IsConnecting = true;
            StatusText = $"Connecting to {Connection.Host}…";

            _ssh?.Dispose();
            _ssh = new SshService(_log) { SshKeysFolder = SshKeysFolder };

            // Notify the View so it can attach the session to the terminal control
            // BEFORE ConnectAsync starts reading data (avoids missing the login banner).
            SessionAttachRequired?.Invoke(this, _ssh);

            _ssh.Disconnected += (_, _) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsConnected = false;
                    StatusText  = "Disconnected";
                    TabCloseRequested?.Invoke();
                });
            };

            _ssh.ErrorOccurred += (_, msg) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    LocalOutput?.Invoke($"\r\n\x1b[91mSSH error: {msg}\x1b[0m\r\n"));
            };

            try
            {
                await _ssh.ConnectAsync(
                    Connection.Host, Connection.Port, Connection.Username,
                    passwordPrompt, cols, rows);

                IsConnected  = true;
                IsConnecting = false;
                StatusText   = $"Connected: {Connection.Username}@{Connection.Host}";
                Header       = Connection.Name;
                MachineInfo  = _ssh.MachineInfo;
                Connection.LastConnected = DateTime.Now.ToString("o");
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                StatusText   = $"Failed: {ex.Message}";
                Header       = Connection?.Name ?? "Terminal";
                LocalOutput?.Invoke($"\r\n\x1b[91mConnection failed: {ex.Message}\x1b[0m\r\n");
            }
        }

        /// <summary>Send a string to the active SSH session (e.g. from the wiki command runner).</summary>
        public void SendData(string data) => _ssh?.Send(data);

        /// <summary>Run a command out-of-band (not through the interactive shell) and return its output.</summary>
        public Task<string> RunCommandAsync(string cmd)
            => _ssh?.RunCommandAsync(cmd) ?? Task.FromResult(string.Empty);

        /// <summary>Read a remote file's content via SSH exec channel.</summary>
        public Task<(string Content, bool Success)> ReadFileAsync(string path, string sudoPassword = "")
            => _ssh?.ReadFileAsync(path, sudoPassword) ?? Task.FromResult((string.Empty, false));

        /// <summary>Write content to a remote file via SSH exec channel.</summary>
        public Task<bool> WriteFileAsync(string path, string content, string sudoPassword = "")
            => _ssh?.WriteFileAsync(path, content, sudoPassword) ?? Task.FromResult(false);

        public void Disconnect()
        {
            _ssh?.Disconnect();
            IsConnected = false;
            StatusText  = "Disconnected";
            Header      = Connection?.Name ?? "Terminal";
        }

        public async Task RefreshMachineInfoAsync()
        {
            if (_ssh != null && IsConnected)
            {
                await _ssh.RefreshMachineInfoAsync();
                MachineInfo = _ssh.MachineInfo;
                OnPropertyChanged(nameof(CurrentDirectory));
                OnPropertyChanged(nameof(DiskSizes));
            }
        }

        public void Dispose()
        {
            _ssh?.Dispose();
        }
    }
}
