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
        private bool _isConnected;
        private bool _isConnecting;
        private string _statusText = "Disconnected";
        private MachineInfo? _machineInfo;

        // ── Inline password capture ───────────────────────────────────────────
        private volatile bool _capturingPassword;
        private readonly StringBuilder _passwordBuffer = new();
        private SemaphoreSlim _passwordInputReady = new(0, 1);

        /// <summary>
        /// When true the tab view will call <see cref="ConnectAsync"/> as soon as it loads.
        /// Set by <c>MainViewModel.ConnectToConnection</c>; cleared immediately in OnLoaded
        /// so that tab-switch view-recreations do not trigger a second connection attempt.
        /// </summary>
        public bool AutoConnectOnLoad { get; set; }

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

        public string Header
        {
            get => _header;
            set => Set(ref _header, value);
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

            // Reset inline password capture state for this connection attempt.
            _capturingPassword = false;
            _passwordBuffer.Clear();
            var oldSemaphore = _passwordInputReady;
            _passwordInputReady = new SemaphoreSlim(0, 1);
            oldSemaphore.Dispose();

            IsConnecting = true;
            StatusText = $"Connecting to {Connection.Host}…";
            WriteToTerminal($"\r\nConnecting to {Connection.Username}@{Connection.Host}:{Connection.Port}...\r\n");

            try
            {
                _ssh?.Dispose();
                _ssh = new SshService(_log);

                // Password prompt: write the server's prompt into the terminal and
                // wait for the user to type the password and press Enter.
                _ssh.PasswordPrompt = prompt =>
                {
                    WriteToTerminal(prompt);
                    _capturingPassword = true;
                    // Block the SSH background thread until the user presses Enter
                    // (or for at most 2 minutes to avoid an indefinite hang).
                    _passwordInputReady.Wait(TimeSpan.FromMinutes(2));
                    return _passwordBuffer.ToString();
                };

                _ssh.DataReceived += data => Application.Current?.Dispatcher.Invoke(() => TerminalDataReceived?.Invoke(data));
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
                // If authentication was in progress, unblock the capture semaphore.
                if (_capturingPassword)
                {
                    _capturingPassword = false;
                    _passwordBuffer.Clear();
                    _passwordInputReady.Release();
                }

                IsConnecting = false;
                StatusText   = $"Failed: {ex.Message}";
                Header       = $"✕ {Connection?.Name ?? "Terminal"}";
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
            if (_capturingPassword)
            {
                foreach (char ch in data)
                {
                    if (ch == '\r')                      // Enter — submit password
                    {
                        _capturingPassword = false;
                        LocalOutput?.Invoke("\r\n");     // move to next line
                        _passwordInputReady.Release();
                        return;
                    }
                    else if (ch == '\x03')               // Ctrl+C — cancel
                    {
                        _capturingPassword = false;
                        _passwordBuffer.Clear();
                        LocalOutput?.Invoke("^C\r\n");
                        _passwordInputReady.Release();
                        return;
                    }
                    else if (ch == '\x7f' || ch == '\b') // Backspace
                    {
                        if (_passwordBuffer.Length > 0)
                            _passwordBuffer.Remove(_passwordBuffer.Length - 1, 1);
                        // No echo — password stays hidden
                    }
                    else if (ch >= ' ')                  // Printable character
                    {
                        _passwordBuffer.Append(ch);
                        // No echo — do not show asterisks or the character
                    }
                }
                return;
            }

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
            _passwordInputReady.Dispose();
        }
    }
}
