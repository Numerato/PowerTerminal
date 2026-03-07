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
                    Wiki.ActiveTerminal = value;
            }
        }

        public int RightTabIndex
        {
            get => _rightTabIndex;
            set => Set(ref _rightTabIndex, value);
        }

        private void LoadDefaultTabs()
        {
            var connections = _config.LoadConnections();
            if (connections.Count > 0)
            {
                foreach (var conn in connections)
                    AddTabForConnection(conn);
            }
            else
            {
                AddNewTab();
            }
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

        private void ConnectSelectedConnection()
        {
            var conn = ConnectionManager.Selected;
            if (conn == null) return;
            // Open a new tab or use an existing disconnected tab
            var tab = TerminalTabs.FirstOrDefault(t => !t.IsConnected && t.Connection?.Id == conn.Id)
                   ?? AddTabForConnection(conn);
            tab.Connection = conn;
            tab.ConnectCommand.Execute(null);
            ActiveTerminalTab = tab;
        }

        private void OpenWikiEditorForNew()
        {
            WikiEditor.LoadNew();
            OpenWikiEditorRequested?.Invoke(WikiEditor);
        }

        private void OpenWikiEditorForEdit()
        {
            if (Wiki.SelectedEntry == null) return;
            WikiEditor.LoadExisting(Wiki.SelectedEntry);
            OpenWikiEditorRequested?.Invoke(WikiEditor);
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
