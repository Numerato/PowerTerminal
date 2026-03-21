using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.ViewModels
{
    public class WikiViewModel : ViewModelBase
    {
        private readonly WikiService _wiki;
        private readonly LoggingService _log;
        private string _searchQuery = string.Empty;
        private WikiEntry? _selectedEntry;
        private TerminalTabViewModel? _activeTerminal;

        public WikiViewModel(WikiService wiki, LoggingService log)
        {
            _wiki = wiki;
            _log  = log;

            SearchCommand = new RelayCommand(_ => PerformSearch());
            ClearSearchCommand = new RelayCommand(_ => { SearchQuery = string.Empty; PerformSearch(); });

            PerformSearch();
        }

        public ObservableCollection<WikiEntry> SearchResults { get; } = new();
        public ICommand SearchCommand      { get; }
        public ICommand ClearSearchCommand { get; }

        /// <summary>Set by MainViewModel to route copy/execute commands to active terminal.</summary>
        public TerminalTabViewModel? ActiveTerminal
        {
            get => _activeTerminal;
            set => Set(ref _activeTerminal, value);
        }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (Set(ref _searchQuery, value))
                    PerformSearch();
            }
        }

        public WikiEntry? SelectedEntry
        {
            get => _selectedEntry;
            set => Set(ref _selectedEntry, value);
        }

        private void PerformSearch()
        {
            var results = _wiki.Search(_searchQuery);
            SearchResults.Clear();
            foreach (var e in results) SearchResults.Add(e);

            if (SelectedEntry != null && !SearchResults.Contains(SelectedEntry))
                SelectedEntry = SearchResults.FirstOrDefault();
        }

        /// <summary>Copy a command to the active terminal (with variable substitution).</summary>
        public void CopyCommand(WikiEntry wiki, string command)
        {
            string resolved = ResolveVariables(command);
            _activeTerminal?.SendData(resolved);
            _log.LogWikiCommandCopied(wiki.Title, resolved);
        }

        /// <summary>Copy a command to the active terminal and press Enter.</summary>
        public void ExecuteCommand(WikiEntry wiki, string command)
        {
            string resolved = ResolveVariables(command);
            _activeTerminal?.SendData(resolved + "\r");
            _log.LogWikiCommandExecuted(wiki.Title, resolved);
        }

        /// <summary>
        /// Resolves predefined machine variables ($CurrentDirectory$, etc.) and
        /// user variables ($variable:Name$).
        /// </summary>
        public string ResolveVariables(string command)
        {
            if (_activeTerminal == null) return command;

            // Predefined variables
            var predefined = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["$CurrentDirectory$"] = _activeTerminal.CurrentDirectory,
                ["$OperatingSystem$"]  = _activeTerminal.OperatingSystem,
                ["$version$"]          = _activeTerminal.OsVersion,
                ["$homefolder$"]       = _activeTerminal.HomeFolder,
                ["$hardware$"]         = _activeTerminal.Hardware,
                ["$disksizes$"]        = _activeTerminal.DiskSizes,
                ["$ipaddress$"]        = _activeTerminal.IpAddress,
                ["$hostname$"]         = _activeTerminal.Hostname,
                ["$cpu$"]              = _activeTerminal.CpuInfo,
                ["$memory$"]           = _activeTerminal.TotalMemory,
                ["$username$"]         = _activeTerminal.Username,
                ["$uptime$"]           = _activeTerminal.Uptime,
                ["$kernelversion$"]    = _activeTerminal.KernelVersion,
            };

            string result = command;
            foreach (var kv in predefined)
                result = result.Replace(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase);

            // User variables: $variable:Name$
            var varPattern = new Regex(@"\$variable:([^$]+)\$", RegexOptions.IgnoreCase);
            result = varPattern.Replace(result, m =>
            {
                string varName = m.Groups[1].Value;
                return PromptForVariable(varName) ?? m.Value;
            });

            return result;
        }

        /// <summary>
        /// Shows an input dialog to get a user variable value.
        /// Override or hook into this in the View layer.
        /// </summary>
        public Func<string, string?>? VariablePromptCallback { get; set; }

        private string? PromptForVariable(string name)
        {
            return VariablePromptCallback?.Invoke(name);
        }
    }
}
