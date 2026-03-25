using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.ViewModels
{
    public class ConnectionManagerViewModel : ViewModelBase
    {
        private readonly ConfigService _config;
        private SshConnection? _selected;
        private SshConnection? _editing;

        public ConnectionManagerViewModel(ConfigService config)
        {
            _config = config;
            Connections = new ObservableCollection<SshConnection>(config.LoadConnections());

            AddCommand    = new RelayCommand(_ => StartAdd());
            DeleteCommand = new RelayCommand(_ => Delete(), _ => Selected != null);
            CopyCommand   = new RelayCommand(_ => CopySelected(), _ => Selected != null);
            SaveCommand   = new RelayCommand(_ => SaveEdit(), _ => Editing != null);
            CancelCommand = new RelayCommand(_ => CancelEdit(), _ => Editing != null);

            LoadIconOptions();

            // Auto-select the first connection so the form is immediately populated.
            if (Connections.Count > 0)
            {
                _suppressAutoEdit = true;
                Selected = Connections[0];
                _suppressAutoEdit = false;
                StartEdit();
            }
            else
            {
                StartAdd();
            }
        }

        /// <summary>
        /// Called every time the Connection Manager window is shown.
        /// Resets state for re-opened windows: auto-starts Add if the list is empty.
        /// </summary>
        public void OnWindowShown()
        {
            if (Connections.Count == 0 && Editing == null)
                StartAdd();
        }

        public ObservableCollection<SshConnection> Connections { get; }
        public ICommand AddCommand    { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CopyCommand   { get; }
        public ICommand SaveCommand   { get; }
        public ICommand CancelCommand { get; }

        /// <summary>Predefined icon choices shown in the icon picker ComboBox.</summary>
        public ObservableCollection<IconOption> IconOptions { get; } = new();

        public SshConnection? Selected
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value) && value != null && !_suppressAutoEdit)
                    StartEdit();
            }
        }

        public SshConnection? Editing
        {
            get => _editing;
            set => Set(ref _editing, value);
        }

        public bool IsEditing => Editing != null;

        /// <summary>Raised after Copy or Add to tell the view to focus the Name field.</summary>
        public event Action FocusNameFieldRequested;

        private bool _isAddMode;
        public bool IsAddMode => _isAddMode;

        private void StartAdd()
        {
            _isAddMode = true;
            Editing = new SshConnection
            {
                Name     = "New Connection",
                Port     = 22,
                LogoPath = GetDefaultIconPath("linux.ico")
            };
            FocusNameFieldRequested?.Invoke();
        }

        private void CopySelected()
        {
            if (Selected == null) return;
            _isAddMode = true;
            Editing = new SshConnection
            {
                Name     = $"Copy of {Selected.Name}",
                Host     = Selected.Host,
                Username = Selected.Username,
                Port     = Selected.Port,
                LogoPath = Selected.LogoPath
            };
            FocusNameFieldRequested?.Invoke();
        }

        private void StartEdit()
        {
            if (Selected == null) return;
            _isAddMode = false;
            Editing = new SshConnection
            {
                Id       = Selected.Id,
                Name     = Selected.Name,
                Host     = Selected.Host,
                Username = Selected.Username,
                Port     = Selected.Port,
                LogoPath = Selected.LogoPath
            };
        }

        private void SaveEdit()
        {
            if (Editing == null) return;
            if (_isAddMode)
            {
                Connections.Add(Editing);
                // Suppress re-triggering edit when Selected is set after add
                _suppressAutoEdit = true;
                Selected = Editing;
                _suppressAutoEdit = false;
            }
            else
            {
                var existing = Connections.FirstOrDefault(c => c.Id == Editing.Id);
                if (existing != null)
                {
                    int idx = Connections.IndexOf(existing);
                    Connections[idx] = Editing;
                    _suppressAutoEdit = true;
                    Selected = Editing;
                    _suppressAutoEdit = false;
                }
            }
            _config.SaveConnections(Connections);
            // Reload the form from the saved connection so fields stay populated.
            StartEdit();
        }

        private void CancelEdit()
        {
            Editing = null;
        }

        private void Delete()
        {
            if (Selected == null) return;
            Connections.Remove(Selected);
            _config.SaveConnections(Connections);
            _suppressAutoEdit = true;
            Editing  = null;
            Selected = null;
            _suppressAutoEdit = false;

            if (Connections.Count > 0)
            {
                Selected = Connections[0];
                StartEdit();
            }
            else
            {
                StartAdd();
            }
        }

        // ── Icon helpers ────────────────────────────────────────────────────

        // ── Reorder ─────────────────────────────────────────────────────────

        public void MoveConnection(SshConnection item, SshConnection target)
        {
            int from = Connections.IndexOf(item);
            int to   = Connections.IndexOf(target);
            if (from < 0 || to < 0 || from == to) return;
            Connections.Move(from, to);
            _config.SaveConnections(Connections);
        }

        public void MoveSelectedUp()
        {
            if (Selected == null) return;
            int idx = Connections.IndexOf(Selected);
            if (idx <= 0) return;
            Connections.Move(idx, idx - 1);
            _config.SaveConnections(Connections);
        }

        public void MoveSelectedDown()
        {
            if (Selected == null) return;
            int idx = Connections.IndexOf(Selected);
            if (idx < 0 || idx >= Connections.Count - 1) return;
            Connections.Move(idx, idx + 1);
            _config.SaveConnections(Connections);
        }

        private bool _suppressAutoEdit;

        private void LoadIconOptions()
        {
            IconOptions.Clear();
            string iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico");
            if (!Directory.Exists(iconsDir)) return;

            var files = Directory
                .GetFiles(iconsDir, "*.*")
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".ico")
                .OrderBy(f => string.Equals(Path.GetFileName(f), "linux.ico",
                                            StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(Path.GetFileName);

            foreach (var path in files)
            {
                // Store as relative path so connections.json stays portable
                string relativePath = Path.Combine("ico", Path.GetFileName(path));
                IconOptions.Add(new IconOption
                {
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    Path        = relativePath
                });
            }
        }

        private static string? GetDefaultIconPath(string fileName)
        {
            // Return relative path; resolved to full path at display time
            string relativePath = Path.Combine("ico", fileName);
            string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            return File.Exists(fullPath) ? relativePath : null;
        }

        /// <summary>
        /// Resolves a LogoPath (relative or absolute) to a full file-system path for image display.
        /// </summary>
        public static string ResolveIconPath(string? logoPath)
        {
            if (string.IsNullOrEmpty(logoPath)) return string.Empty;
            if (Path.IsPathRooted(logoPath)) return logoPath; // custom full path
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logoPath);
        }
    }
}
