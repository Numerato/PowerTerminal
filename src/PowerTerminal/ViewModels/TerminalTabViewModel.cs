using System;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;

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
        /// Set by <c>MainViewModel.ConnectToConnection</c>; cleared immediately in OnLoaded
        /// so that tab-switch view-recreations do not trigger a second connection attempt.
        /// </summary>
        public bool AutoConnectOnLoad { get; set; }

        /// <summary>
        /// Global SSH keys folder passed through to <see cref="SshService.SshKeysFolder"/>.
        /// Set by <see cref="MainViewModel"/> from <see cref="AppSettings.SshKeysFolder"/>.
        /// </summary>
        public string SshKeysFolder { get; set; } =
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

        /// <summary>
        /// Visual settings for the terminal (font, size, colors, padding).
        /// Set by <see cref="MainViewModel"/> on creation.
        /// </summary>
        public ThemeSettings Theme { get; set; } = new();

        public Thickness TerminalPadding => new Thickness(Theme.Padding);

        /// <summary>
        /// Set by the View (TerminalTabView) to enable inline terminal password collection.
        /// Called from a background SSH thread; the implementation must block until the user
        /// submits the password (or cancels).  Receives the prompt text; returns the password
        /// or an empty string if the user cancelled.
        /// </summary>
        public Func<string, string>? InlinePasswordCollector { get; set; }

        public TerminalTabViewModel(LoggingService log)
        {
            _log = log;
            ConnectCommand    = new RelayCommand(async _ => await ConnectAsync(),    _ => !IsConnected && !IsConnecting && Connection != null);
            DisconnectCommand = new RelayCommand(_ => Disconnect(),                  _ => IsConnected);
        }

        public SshConnection? Connection { get; set; }
        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        /// <summary>Raised on the UI thread when data arrives from the shell.</summary>
        public event Action<string>? TerminalDataReceived;
        /// <summary>
        /// Raised on the UI thread to write locally-generated text (prompts, status, errors)
        /// directly into the terminal control without going through the SSH channel.
        /// </summary>
        public event Action<string>? LocalOutput;
        /// <summary>Raised when connection state changes.</summary>
        public event Action? StateChanged;
        /// <summary>
        /// Raised on the UI thread when a connected SSH session ends (cleanly or with an error).
        /// MainViewModel subscribes to this event to remove the tab from the collection.
        /// </summary>
        public event Action? TabCloseRequested;
        /// <summary>
        /// Raised at the start of <see cref="ConnectAsync"/> so the View can clear the terminal
        /// before showing new connection output.
        /// </summary>
        public event Action? ClearRequested;

        public string Header
        {
            get => _header;
            set => Set(ref _header, value);
        }

        /// <summary>
        /// True when this tab is currently the active (visible) tab in the UI.
        /// Set by <see cref="MainViewModel"/> when <see cref="MainViewModel.ActiveTerminalTab"/> changes.
        /// The view binds <c>Visibility</c> to this property so only the active tab's terminal is visible.
        /// </summary>
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

        // ── Shell variables available for wiki substitution ────────────────────
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

        /// <summary>Optional logo image path from the connection configuration.</summary>
        public string? LogoPath => Connection?.LogoPath;

        public async Task ConnectAsync()
        {
            if (Connection == null) return;

            // Clear the terminal and start fresh for this connection attempt
            ClearRequested?.Invoke();

            IsConnecting = true;
            StatusText = $"Connecting to {Connection.Host}…";
            WriteToTerminal($"Connecting to {Connection.Username}@{Connection.Host}:{Connection.Port}...\r\n");

            try
            {
                _ssh?.Dispose();
                _ssh = new SshService(_log)
                {
                    SshKeysFolder = SshKeysFolder
                };

                // Inline password prompt: the SSH background thread calls this callback,
                // which blocks until the user types a password in the terminal and presses Enter.
                _ssh.PasswordPrompt = prompt =>
                {
                    if (InlinePasswordCollector != null)
                        return InlinePasswordCollector(prompt);
                    // View not yet attached — log and abort so the caller gets a clear error.
                    WriteToTerminal("\r\n\x1b[91mPassword prompt unavailable: terminal view not ready.\x1b[0m\r\n");
                    return string.Empty;
                };


                // Fire each SSH chunk immediately. Use non-blocking invocation to keep
                // the SSH pipeline flowing. The UI control (TerminalControl) is now
                // responsible for thread-safe buffering and flow control.
                _ssh.DataReceived += data => TerminalDataReceived?.Invoke(data);
                _ssh.Disconnected += ex =>
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        IsConnected = false;
                        StatusText  = ex != null ? $"Error: {ex.Message}" : "Disconnected";
                        if (ex != null)
                            WriteToTerminal($"\r\n\x1b[91mConnection lost: {ex.Message}\x1b[0m\r\n");
                        TabCloseRequested?.Invoke();
                    });
                };

                await _ssh.ConnectAsync(Connection);
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
                WriteToTerminal($"\r\n\x1b[91mConnection failed: {ex.Message}\x1b[0m\r\n");
            }
        }

        public void Disconnect()
        {
            _ssh?.Disconnect();
            IsConnected = false;
            StatusText  = "Disconnected";
            Header      = Connection?.Name ?? "Terminal";
        }


        public void SendData(string data)
        {
            if (IsConnected) _ssh?.SendData(data);
        }

        /// <summary>
        /// Writes text to the terminal control on the UI thread.
        /// Safe to call from any thread.
        /// </summary>
        private void WriteToTerminal(string text)
        {
            if (Application.Current?.Dispatcher.CheckAccess() == true)
                LocalOutput?.Invoke(text);
            else
                Application.Current?.Dispatcher.Invoke(() => LocalOutput?.Invoke(text));
        }

        public void Resize(uint cols, uint rows)
        {
            _ssh?.Resize(cols, rows);
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
