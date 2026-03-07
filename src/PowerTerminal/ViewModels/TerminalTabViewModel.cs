using System;
using System.Collections.ObjectModel;
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
        /// <summary>Raised when connection state changes.</summary>
        public event Action? StateChanged;

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

        public async Task ConnectAsync()
        {
            if (Connection == null) return;
            IsConnecting = true;
            StatusText = $"Connecting to {Connection.Host}…";
            try
            {
                _ssh?.Dispose();
                _ssh = new SshService(_log);
                _ssh.PasswordPrompt = prompt =>
                {
                    string password = string.Empty;
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        var dlg = new PowerTerminal.Views.SshPasswordPromptWindow(Connection.Username ?? string.Empty, prompt);
                        if (dlg.ShowDialog() == true)
                            password = dlg.Password;
                    });
                    return password;
                };
                _ssh.DataReceived  += data => Application.Current?.Dispatcher.Invoke(() => TerminalDataReceived?.Invoke(data));
                _ssh.Disconnected  += ex =>
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        IsConnected = false;
                        StatusText  = ex != null ? $"Error: {ex.Message}" : "Disconnected";
                        Header      = $"✕ {Connection.Name}";
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
                Header       = $"✕ {Connection?.Name ?? "Terminal"}";
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
