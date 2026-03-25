using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using PowerTerminal.Models;

namespace PowerTerminal.ViewModels
{
    public class RemoteExplorerViewModel : ViewModelBase
    {
        private TerminalTabViewModel? _tab;
        private string _currentPath = "~";
        private bool _useSudo;
        private bool _isLoading;
        private bool _isConnected;
        private string _statusMessage = string.Empty;
        private string _pathBarText = "~";
        private string _sudoPassword = string.Empty;

        public RemoteExplorerViewModel()
        {
            NavigateUpCommand = new RelayCommand(_ => _ = NavigateUpAsync(), _ => IsConnected && CurrentPath != "/");
            RefreshCommand    = new RelayCommand(_ => _ = RefreshAsync(),    _ => IsConnected);
            NavigateCommand   = new RelayCommand(_ => _ = NavigateToPathBarAsync(), _ => IsConnected);
        }

        /// <summary>Raised when a file should be opened in the editor (path, useSudo).</summary>
        public event Action<string, bool>? OpenFileRequested;

        /// <summary>Raised to get a sudo password from the view (message) -> returns password or empty.</summary>
        public Func<string, string>? PasswordPromptCallback;

        public ObservableCollection<RemoteFileItem> Items { get; } = new();

        public string CurrentPath
        {
            get => _currentPath;
            private set { if (Set(ref _currentPath, value)) PathBarText = value; }
        }

        public string PathBarText
        {
            get => _pathBarText;
            set => Set(ref _pathBarText, value);
        }

        public bool UseSudo
        {
            get => _useSudo;
            set => Set(ref _useSudo, value);  // View handles the password + refresh flow
        }

        /// <summary>
        /// Called by the View when the user enables sudo.
        /// Stores the password and refreshes. Returns false if cancelled.
        /// </summary>
        public async System.Threading.Tasks.Task<bool> EnableSudoWithPasswordAsync(string password)
        {
            if (string.IsNullOrEmpty(password)) return false;
            _sudoPassword = password;
            // Use the backing field directly to avoid triggering the setter side-effects
            _useSudo = true;
            OnPropertyChanged(nameof(UseSudo));
            await RefreshAsync();
            return true;
        }

        /// <summary>Disables sudo, clears the cached password, and refreshes.</summary>
        public async System.Threading.Tasks.Task DisableSudoAsync()
        {
            _sudoPassword = string.Empty;
            _useSudo = false;
            OnPropertyChanged(nameof(UseSudo));
            await RefreshAsync();
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => Set(ref _isLoading, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (Set(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(IsDisconnected));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsDisconnected => !_isConnected;

        public string StatusMessage
        {
            get => _statusMessage;
            private set => Set(ref _statusMessage, value);
        }

        public ICommand NavigateUpCommand { get; }
        public ICommand RefreshCommand    { get; }
        public ICommand NavigateCommand   { get; }

        public void SetActiveTab(TerminalTabViewModel? tab)
        {
            if (_tab != null)
                _tab.StateChanged -= OnTabStateChanged;

            _tab = tab;
            IsConnected = tab?.IsConnected == true;

            if (_tab != null)
                _tab.StateChanged += OnTabStateChanged;

            if (IsConnected)
                _ = NavigateToStartAsync();
            else
            {
                Items.Clear();
                CurrentPath = "~";
                StatusMessage = string.Empty;
            }
        }

        private void OnTabStateChanged()
        {
            bool wasConnected = IsConnected;
            IsConnected = _tab?.IsConnected == true;
            if (!wasConnected && IsConnected)
                _ = NavigateToStartAsync();
            else if (wasConnected && !IsConnected)
            {
                Items.Clear();
                StatusMessage = string.Empty;
                _sudoPassword = string.Empty;
            }
        }

        private async System.Threading.Tasks.Task NavigateToStartAsync()
        {
            if (_tab == null) return;
            string pwd = (await _tab.RunCommandAsync("pwd 2>/dev/null")).Trim();
            if (string.IsNullOrEmpty(pwd)) pwd = "~";
            await NavigateToAsync(pwd);
        }

        public async System.Threading.Tasks.Task NavigateToAsync(string path)
        {
            if (_tab == null || !IsConnected) return;

            IsLoading = true;
            StatusMessage = string.Empty;
            try
            {
                string resolved = (await _tab.RunCommandAsync($"cd {ShellQuote(path)} 2>/dev/null && pwd")).Trim();
                if (string.IsNullOrEmpty(resolved))
                {
                    StatusMessage = $"Cannot navigate to: {path}";
                    return;
                }

                string lsCmd = BuildSudoCmd($"ls -la --color=never {ShellQuote(resolved)} 2>&1");
                string output = await _tab.RunCommandAsync(lsCmd);

                if (UseSudo && output.Contains("sudo:") && output.Contains("password"))
                {
                    string pass = GetOrRequestSudoPassword();
                    if (string.IsNullOrEmpty(pass)) { StatusMessage = "Sudo cancelled."; return; }
                    lsCmd = $"echo {ShellQuote(pass)} | sudo -S ls -la --color=never {ShellQuote(resolved)} 2>&1";
                    output = await _tab.RunCommandAsync(lsCmd);
                }

                if (output.TrimStart().StartsWith("ls:") && output.Contains("Permission denied"))
                    StatusMessage = "Permission denied. Enable Sudo in the toolbar.";

                var items = ParseLsOutput(resolved, output, _tab?.Username ?? string.Empty);
                Items.Clear();
                foreach (var item in items) Items.Add(item);
                CurrentPath = resolved;

                if (items.Count == 0 && !output.Contains("total"))
                    StatusMessage = output.Trim().Length > 0 ? output.Trim() : "Empty directory.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string BuildSudoCmd(string cmd)
        {
            if (!UseSudo) return cmd;
            if (!string.IsNullOrEmpty(_sudoPassword))
                return $"echo {ShellQuote(_sudoPassword)} | sudo -S {cmd.TrimStart()}";
            return $"sudo -n {cmd.TrimStart()}";
        }

        private string GetOrRequestSudoPassword()
        {
            if (!string.IsNullOrEmpty(_sudoPassword)) return _sudoPassword;
            string pass = PasswordPromptCallback?.Invoke("Enter sudo password:") ?? string.Empty;
            _sudoPassword = pass;
            return pass;
        }

        private async System.Threading.Tasks.Task NavigateUpAsync()
        {
            string path = CurrentPath.TrimEnd('/');
            if (path == "" || path == "/") return;
            int last = path.LastIndexOf('/');
            string parent = last <= 0 ? "/" : path[..last];
            await NavigateToAsync(parent);
        }

        private async System.Threading.Tasks.Task RefreshAsync()
            => await NavigateToAsync(CurrentPath);

        private async System.Threading.Tasks.Task NavigateToPathBarAsync()
            => await NavigateToAsync(PathBarText);

        public void OpenItem(RemoteFileItem item)
        {
            if (item.IsDirectory)
                _ = NavigateToAsync(item.FullPath);
            else
                OpenFileRequested?.Invoke(item.FullPath, UseSudo);
        }

        private static string ShellQuote(string path)
        {
            if (string.IsNullOrEmpty(path)) return "~";
            return "'" + path.Replace("'", "'\\''") + "'";
        }

        private static readonly Regex AnsiEscape = new(@"\x1B\[[^m]*m", RegexOptions.Compiled);

        private static List<RemoteFileItem> ParseLsOutput(string basePath, string output, string currentUser)
        {
            var items = new List<RemoteFileItem>();
            foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string line = AnsiEscape.Replace(rawLine, "").TrimEnd();
                if (line.StartsWith("total ") || line.Length == 0) continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 9) continue;

                char typeChar = parts[0][0];
                if (typeChar != '-' && typeChar != 'd' && typeChar != 'l') continue;

                string name = string.Join(" ", parts.Skip(8));
                bool isSymlink = typeChar == 'l';
                int arrowIdx = name.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrowIdx >= 0) name = name[..arrowIdx];
                if (name == "." || name == "..") continue;

                long size = long.TryParse(parts[4], out var s) ? s : 0;
                string modified = $"{parts[5]} {parts[6]} {parts[7]}";

                string owner = parts[2];
                string perms = parts[0]; // e.g. "drwxr-xr-x"

                // Determine effective permissions:
                // - if owner == currentUser: use owner bits (index 1,2,3)
                // - otherwise: use other bits (index 7,8,9)
                bool isOwner = string.Equals(owner, currentUser, StringComparison.Ordinal);
                int rIdx = isOwner ? 1 : 7;
                int wIdx = isOwner ? 2 : 8;

                bool canRead  = perms.Length > rIdx && perms[rIdx] == 'r';
                bool canWrite = perms.Length > wIdx && perms[wIdx] == 'w';

                items.Add(new RemoteFileItem
                {
                    Name        = name,
                    FullPath    = basePath.TrimEnd('/') + "/" + name,
                    IsDirectory = typeChar == 'd',
                    IsSymlink   = isSymlink,
                    Size        = size,
                    Permissions = perms,
                    Owner       = owner,
                    CanRead     = canRead,
                    CanWrite    = canWrite,
                    Modified    = modified
                });
            }
            return items
                .OrderByDescending(i => i.IsDirectory)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
