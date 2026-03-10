using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
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
        private int _rightTabIndex;

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

            // Load connections and open default tab
            LoadDefaultTabs();

            // Apply initial settings to services
            RefreshSettings();
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
                    // Keep IsActive in sync so each tab's TerminalTabView shows/hides itself
                    foreach (var tab in TerminalTabs)
                        tab.IsActive = (tab == value);
                    Wiki.ActiveTerminal = value;
                }
            }
        }

        public int RightTabIndex
        {
            get => _rightTabIndex;
            set => Set(ref _rightTabIndex, value);
        }

        private void LoadDefaultTabs()
        {
            // Tabs are opened on demand via the Connect dropdown in the title bar.
        }

        public TerminalTabViewModel AddNewTab()
        {
            var tab = new TerminalTabViewModel(_log) { Header = $"Terminal {TerminalTabs.Count + 1}" };
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
                // InlinePasswordCollector is wired by TerminalTabView.OnDataContextChanged
                // when the tab's view is created — no popup dialog needed.
            };
            tab.TabCloseRequested += () => RemoveTab(tab);
            TerminalTabs.Add(tab);
            ActiveTerminalTab = tab;
        }

        /// <summary>Disconnects, disposes and removes a tab; activates the last remaining tab.</summary>
        public void RemoveTab(TerminalTabViewModel tab)
        {
            if (!TerminalTabs.Contains(tab)) return;
            tab.Disconnect();
            tab.Dispose();
            TerminalTabs.Remove(tab);
            if (ActiveTerminalTab == tab)
                ActiveTerminalTab = TerminalTabs.LastOrDefault();
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
