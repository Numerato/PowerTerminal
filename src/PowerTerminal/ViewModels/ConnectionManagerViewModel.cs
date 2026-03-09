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
            SaveCommand   = new RelayCommand(_ => SaveEdit(), _ => Editing != null);
            CancelCommand = new RelayCommand(_ => CancelEdit(), _ => Editing != null);

            LoadIconOptions();
        }

        public ObservableCollection<SshConnection> Connections { get; }
        public ICommand AddCommand    { get; }
        public ICommand DeleteCommand { get; }
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

        private bool _isAddMode;

        private void StartAdd()
        {
            _isAddMode = true;
            Editing = new SshConnection
            {
                Name     = "New Connection",
                Port     = 22,
                LogoPath = GetDefaultIconPath("linux.png")
            };
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
            Editing = null;
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
        }

        // ── Icon helpers ────────────────────────────────────────────────────

        private bool _suppressAutoEdit;

        private void LoadIconOptions()
        {
            IconOptions.Clear();
            string iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons");
            if (!Directory.Exists(iconsDir)) return;

            var files = Directory
                .GetFiles(iconsDir, "*.*")
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".ico")
                .OrderBy(f => string.Equals(Path.GetFileName(f), "linux.png",
                                            StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(Path.GetFileName);

            foreach (var path in files)
            {
                IconOptions.Add(new IconOption
                {
                    DisplayName = Path.GetFileNameWithoutExtension(path),
                    Path        = path
                });
            }
        }

        private static string? GetDefaultIconPath(string fileName)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons", fileName);
            return File.Exists(path) ? path : null;
        }
    }
}
