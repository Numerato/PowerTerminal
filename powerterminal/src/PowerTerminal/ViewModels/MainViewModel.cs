using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ConfigService _config;
        private readonly LoggingService _log;
        private readonly AiService _ai;
        private readonly WikiService _wiki;
        private TerminalTabViewModel? _activeTerminalTab;
        private string? _activePanel = null; // null = closed

        public MainViewModel()
        {
            _config = new ConfigService();
            _log    = new LoggingService(_config.LogDir);

            var settings = _config.LoadSettings();
            _ai   = new AiService(_log, settings);
            _wiki = new WikiService(_config, _log);
            _wiki.LoadAll();

            AiChat = new AiChatViewModel(_ai, _log);
            Wiki   = new WikiViewModel(_wiki, _log);
            ConnectionManager = new ConnectionManagerViewModel(_config);
            WikiEditor = new WikiEditorViewModel(_wiki);

            // Wire wiki to prompt for variables via dialog
            Wiki.VariablePromptCallback = PromptVariable;
            WikiEditor.SaveRequested   += () => { _wiki.LoadAll(); Wiki.SearchQuery = string.Empty; };

            AddTabCommand              = new RelayCommand(_ => AddNewTab());
            OpenConnectionManagerCommand = new RelayCommand(_ => OpenConnectionManagerRequested?.Invoke());
            OpenSettingsCommand        = new RelayCommand(_ => OpenSettingsRequested?.Invoke());
            OpenWikiEditorCommand      = new RelayCommand(_ => OpenWikiEditorForNew());
            EditWikiCommand            = new RelayCommand(_ => OpenWikiEditorForEdit(), _ => Wiki.SelectedEntry != null);
            DeleteWikiCommand          = new RelayCommand(_ => DeleteWiki(), _ => Wiki.SelectedEntry != null);
            ConnectSelectedCommand     = new RelayCommand(_ => ConnectSelectedConnection(),
                                                          _ => ConnectionManager.Selected != null);
            TogglePanelCommand         = new RelayCommand(p => TogglePanel(p as string));

            // Load connections and open default tab
            LoadDefaultTabs();
        }

        public void RefreshSettings()
        {
            var settings = _config.LoadSettings();
            _log.EnableDebugLogging = settings.EnableDebugLogging;
        }

        public AiChatViewModel          AiChat            { get; }
        public WikiViewModel            Wiki              { get; }
        public ConnectionManagerViewModel ConnectionManager { get; }
        public WikiEditorViewModel      WikiEditor        { get; }

        public ICommand AddTabCommand                 { get; }
        public ICommand OpenConnectionManagerCommand  { get; }
        public ICommand OpenSettingsCommand           { get; }
        public ICommand OpenWikiEditorCommand         { get; }
        public ICommand EditWikiCommand               { get; }
        public ICommand DeleteWikiCommand             { get; }
        public ICommand ConnectSelectedCommand        { get; }
        public ICommand TogglePanelCommand            { get; }

        // Events to trigger window/dialog opening from View
        public event Action? OpenConnectionManagerRequested;
        public event Action? OpenSettingsRequested;
        public event Action<WikiEditorViewModel>? OpenWikiEditorRequested;
        public event Func<string, string?>? VariablePromptRequested;

        public ObservableCollection<TerminalTabViewModel> TerminalTabs { get; } = new();

        public TerminalTabViewModel? ActiveTerminalTab
        {
            get => _activeTerminalTab;
            set
            {
                if (Set(ref _activeTerminalTab, value))
                {
                    foreach (var tab in TerminalTabs)
                        tab.IsActive = (tab == value);
                    Wiki.ActiveTerminal = value;
                    OnPropertyChanged(nameof(WindowTitle));
                    OnPropertyChanged(nameof(WindowIcon));
                }
            }
        }

        /// <summary>Window title shown in the taskbar: active tab name or default app name.</summary>
        public string WindowTitle => _activeTerminalTab?.Header ?? "PowerTerminal";

        /// <summary>Taskbar icon: tab logo when set, otherwise the default app icon.</summary>
        public ImageSource? WindowIcon
        {
            get
            {
                var logoPath = _activeTerminalTab?.LogoPath;
                if (!string.IsNullOrEmpty(logoPath))
                {
                    try { return new BitmapImage(new Uri(logoPath, UriKind.RelativeOrAbsolute)); }
                    catch { /* fall through to default */ }
                }
                return null; // null → WPF uses the Window's own Icon from XAML
            }
        }

        /// <summary>Currently open sidebar panel key, or null when the panel is closed.</summary>
        public string? ActivePanel
        {
            get => _activePanel;
            private set
            {
                if (Set(ref _activePanel, value))
                {
                    OnPropertyChanged(nameof(IsPanelOpen));
                    OnPropertyChanged(nameof(WikiPanelActive));
                    OnPropertyChanged(nameof(AiChatPanelActive));
                    OnPropertyChanged(nameof(ExplorerPanelActive));
                    OnPropertyChanged(nameof(SettingsPanelActive));
                }
            }
        }

        public bool IsPanelOpen        => _activePanel != null;
        public bool WikiPanelActive    => _activePanel == "wiki";
        public bool AiChatPanelActive  => _activePanel == "aichat";
        public bool ExplorerPanelActive=> _activePanel == "explorer";
        public bool SettingsPanelActive=> _activePanel == "settings";

        private void TogglePanel(string? key)
        {
            ActivePanel = (ActivePanel == key) ? null : key;
        }

        private void LoadDefaultTabs()
        {
            // Tabs are opened on demand via the Connect dropdown in the title bar.
        }

        private TerminalTabViewModel AddNewTab()
        {
            var settings = _config.LoadSettings();
            var connection = new SshConnection { Name = "Local Terminal" };
            var tab = new TerminalTabViewModel(_log)
            {
                Connection = connection,
                Header     = connection.Name,
                Theme      = settings.Theme
            };
            TerminalTabs.Add(tab);
            ActiveTerminalTab = tab;
            return tab;
        }

        private TerminalTabViewModel AddTabForConnection(SshConnection connection)
        {
            var tab = new TerminalTabViewModel(_log)
            {
                Connection = connection,
                Header     = connection.Name
            };
            TerminalTabs.Add(tab);
            ActiveTerminalTab = tab;
            return tab;
        }

        /// <summary>
        /// Creates a new tab for <paramref name="conn"/>, auto-connects on first load,
        /// and wires <see cref="TerminalTabViewModel.TabCloseRequested"/> to remove the tab
        /// when the SSH session ends.
        /// </summary>
        public void ConnectToConnection(SshConnection conn)
        {
            var settings = _config.LoadSettings();
            var tab = new TerminalTabViewModel(_log)
            {
                Connection        = conn,
                Header            = conn.Name,
                AutoConnectOnLoad = true,
                SshKeysFolder     = settings.SshKeysFolder,
                Theme             = settings.Theme
                // InlinePasswordCollector is wired by TerminalTabView.OnDataContextChanged
                // when the tab's view is created — no popup dialog needed.
            };
            tab.TabCloseRequested += () => RemoveTab(tab);
            TerminalTabs.Add(tab);
            ActiveTerminalTab = tab;
        }

        /// <summary>Disconnects, disposes and removes a tab; activates the previous tab.</summary>
        public void RemoveTab(TerminalTabViewModel tab)
        {
            if (!TerminalTabs.Contains(tab)) return;
            int idx = TerminalTabs.IndexOf(tab);
            tab.Disconnect();
            tab.Dispose();
            TerminalTabs.Remove(tab);
            if (ActiveTerminalTab == tab)
            {
                // Activate the tab that was directly before the closed one, or the new last tab.
                ActiveTerminalTab = TerminalTabs.Count > 0
                    ? TerminalTabs[Math.Max(0, idx - 1)]
                    : null;
            }
        }

        private void ConnectSelectedConnection()
        {
            var conn = ConnectionManager.Selected;
            if (conn == null) return;
            ConnectToConnection(conn);
        }

        private void OpenWikiEditorForNew()
        {
            WikiEditor.LoadNew();
            PopulateEditorVariables();
            OpenWikiEditorRequested?.Invoke(WikiEditor);
        }

        private void OpenWikiEditorForEdit()
        {
            if (Wiki.SelectedEntry == null) return;
            WikiEditor.LoadExisting(Wiki.SelectedEntry);
            PopulateEditorVariables();
            OpenWikiEditorRequested?.Invoke(WikiEditor);
        }

        private void PopulateEditorVariables()
        {
            var t = ActiveTerminalTab;
            WikiEditor.SetVariables(new[]
            {
                new Models.VariableItem { Name = "$currentdirectory$", Value = t?.CurrentDirectory ?? string.Empty },
                new Models.VariableItem { Name = "$operatingsystem$",  Value = t?.OperatingSystem  ?? string.Empty },
                new Models.VariableItem { Name = "$version$",          Value = t?.OsVersion        ?? string.Empty },
                new Models.VariableItem { Name = "$homefolder$",       Value = t?.HomeFolder       ?? string.Empty },
                new Models.VariableItem { Name = "$hardware$",         Value = t?.Hardware         ?? string.Empty },
                new Models.VariableItem { Name = "$disksizes$",        Value = t?.DiskSizes        ?? string.Empty },
                new Models.VariableItem { Name = "$ipaddress$",        Value = t?.IpAddress        ?? string.Empty },
                new Models.VariableItem { Name = "$hostname$",         Value = t?.Hostname         ?? string.Empty },
                new Models.VariableItem { Name = "$cpu$",              Value = t?.CpuInfo          ?? string.Empty },
                new Models.VariableItem { Name = "$memory$",           Value = t?.TotalMemory      ?? string.Empty },
                new Models.VariableItem { Name = "$username$",         Value = t?.Username         ?? string.Empty },
                new Models.VariableItem { Name = "$uptime$",           Value = t?.Uptime           ?? string.Empty },
                new Models.VariableItem { Name = "$kernelversion$",    Value = t?.KernelVersion    ?? string.Empty },
            });
        }

        private void DeleteWiki()
        {
            if (Wiki.SelectedEntry == null) return;
            _wiki.Delete(Wiki.SelectedEntry);
            Wiki.SearchQuery = string.Empty;
        }

        private string? PromptVariable(string name)
        {
            return VariablePromptRequested?.Invoke(name);
        }
    }
}
