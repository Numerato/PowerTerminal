using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
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
            Variables = new VariablesViewModel(_config);
            ConnectionManager = new ConnectionManagerViewModel(_config);
            WikiEditor = new WikiEditorViewModel(_wiki);

            // Wire wiki to prompt for variables via dialog
            Wiki.VariablePromptCallback = PromptVariable;
            // Wire wiki to resolve custom variables from the Variables panel
            Wiki.CustomVariablesProvider = () => Variables.CustomVariables;
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
        public VariablesViewModel       Variables         { get; }
        public ConnectionManagerViewModel ConnectionManager { get; }
        public WikiEditorViewModel      WikiEditor        { get; }
        public RemoteExplorerViewModel? Explorer => _activeTerminalTab?.Explorer;
        public ConfigService            Config    => _config;

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
                var previous = _activeTerminalTab;
                if (Set(ref _activeTerminalTab, value))
                {
                    // Unsubscribe from old tab so we don't leak listeners.
                    if (previous != null)
                        previous.PropertyChanged -= OnActiveTabPropertyChanged;
                    // Subscribe to new tab — refreshes variables when MachineInfo arrives.
                    if (value != null)
                        value.PropertyChanged += OnActiveTabPropertyChanged;

                    foreach (var tab in TerminalTabs)
                        tab.IsActive = (tab == value);
                    Wiki.ActiveTerminal = value;
                    Variables.RefreshSystemVariables(value);
                    OnPropertyChanged(nameof(Explorer));
                }
            }
        }

        /// <summary>
        /// Fires whenever the active tab raises <see cref="System.ComponentModel.INotifyPropertyChanged"/>.
        /// Used to re-populate the Variables panel as soon as <see cref="TerminalTabViewModel.MachineInfo"/>
        /// is set (i.e. immediately after the SSH handshake completes).
        /// </summary>
        private void OnActiveTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TerminalTabViewModel.MachineInfo))
                Variables.RefreshSystemVariables(_activeTerminalTab);
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
                    OnPropertyChanged(nameof(VariablesPanelActive));
                }
            }
        }

        public bool IsPanelOpen        => _activePanel != null;
        public bool WikiPanelActive    => _activePanel == "wiki";
        public bool AiChatPanelActive  => _activePanel == "aichat";
        public bool ExplorerPanelActive=> _activePanel == "explorer";
        public bool SettingsPanelActive=> _activePanel == "settings";
        public bool VariablesPanelActive => _activePanel == "variables";

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
                Connection      = connection,
                Header          = connection.Name,
                Theme           = settings.Theme,
                EnablePowerEdit = settings.EnablePowerEdit,
                CopyPasteMode   = settings.TerminalCopyPasteMode
            };
            TerminalTabs.Add(tab);
            ActiveTerminalTab = tab;
            return tab;
        }

        private TerminalTabViewModel AddTabForConnection(SshConnection connection)
        {
            var settings = _config.LoadSettings();
            var tab = new TerminalTabViewModel(_log)
            {
                Connection      = connection,
                Header          = connection.Name,
                EnablePowerEdit = settings.EnablePowerEdit,
                CopyPasteMode   = settings.TerminalCopyPasteMode
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
                Theme             = settings.Theme,
                EnablePowerEdit   = settings.EnablePowerEdit,
                CopyPasteMode     = settings.TerminalCopyPasteMode
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
            var sysVars = new[]
            {
                new VariableItem { Name = "$currentdirectory$", Value = t?.CurrentDirectory ?? string.Empty },
                new VariableItem { Name = "$operatingsystem$",  Value = t?.OperatingSystem  ?? string.Empty },
                new VariableItem { Name = "$version$",          Value = t?.OsVersion        ?? string.Empty },
                new VariableItem { Name = "$homefolder$",       Value = t?.HomeFolder       ?? string.Empty },
                new VariableItem { Name = "$hardware$",         Value = t?.Hardware         ?? string.Empty },
                new VariableItem { Name = "$disksizes$",        Value = t?.DiskSizes        ?? string.Empty },
                new VariableItem { Name = "$ipaddress$",        Value = t?.IpAddress        ?? string.Empty },
                new VariableItem { Name = "$hostname$",         Value = t?.Hostname         ?? string.Empty },
                new VariableItem { Name = "$cpu$",              Value = t?.CpuInfo          ?? string.Empty },
                new VariableItem { Name = "$memory$",           Value = t?.TotalMemory      ?? string.Empty },
                new VariableItem { Name = "$username$",         Value = t?.Username         ?? string.Empty },
                new VariableItem { Name = "$kernelversion$",    Value = t?.KernelVersion    ?? string.Empty },
                new VariableItem { Name = "$defaultshell$",     Value = t?.DefaultShell     ?? string.Empty },
                new VariableItem { Name = "$timezone$",         Value = t?.Timezone         ?? string.Empty },
                new VariableItem { Name = "$cpucount$",         Value = t?.CpuCount         ?? string.Empty },
                new VariableItem { Name = "$freememory$",       Value = t?.FreeMemory       ?? string.Empty },
                new VariableItem { Name = "$freedisk$",         Value = t?.FreeDisk         ?? string.Empty },
                new VariableItem { Name = "$publicip$",         Value = t?.PublicIp         ?? string.Empty },
                new VariableItem { Name = "$sudouser$",         Value = t?.SudoUser         ?? string.Empty },
            };

            // Combine system variables with custom variables for the editor reference panel.
            var allVars = new System.Collections.Generic.List<VariableItem>(sysVars);
            foreach (var cv in Variables.CustomVariables)
                allVars.Add(new VariableItem { Name = cv.Name, Value = cv.Value });

            WikiEditor.SetVariables(allVars);
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
