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

        // Compiled once; avoids re-parsing the pattern on every command resolution.
        private static readonly Regex VarPattern =
            new(@"\$variable:([^$]+)\$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Resolves predefined machine variables ($CurrentDirectory$, etc.) and
        /// user variables ($variable:Name$).
        /// </summary>
        public string ResolveVariables(string command)
        {
            if (_activeTerminal == null) return command;

            // Chain Replace calls directly — avoids allocating a Dictionary on every invocation.
            string result = command
                .Replace("$CurrentDirectory$", _activeTerminal.CurrentDirectory, StringComparison.OrdinalIgnoreCase)
                .Replace("$OperatingSystem$",  _activeTerminal.OperatingSystem,  StringComparison.OrdinalIgnoreCase)
                .Replace("$version$",          _activeTerminal.OsVersion,        StringComparison.OrdinalIgnoreCase)
                .Replace("$homefolder$",        _activeTerminal.HomeFolder,       StringComparison.OrdinalIgnoreCase)
                .Replace("$hardware$",          _activeTerminal.Hardware,         StringComparison.OrdinalIgnoreCase)
                .Replace("$disksizes$",         _activeTerminal.DiskSizes,        StringComparison.OrdinalIgnoreCase)
                .Replace("$ipaddress$",         _activeTerminal.IpAddress,        StringComparison.OrdinalIgnoreCase)
                .Replace("$hostname$",          _activeTerminal.Hostname,         StringComparison.OrdinalIgnoreCase)
                .Replace("$cpu$",               _activeTerminal.CpuInfo,          StringComparison.OrdinalIgnoreCase)
                .Replace("$memory$",            _activeTerminal.TotalMemory,      StringComparison.OrdinalIgnoreCase)
                .Replace("$username$",          _activeTerminal.Username,         StringComparison.OrdinalIgnoreCase)
                .Replace("$uptime$",            _activeTerminal.Uptime,           StringComparison.OrdinalIgnoreCase)
                .Replace("$kernelversion$",     _activeTerminal.KernelVersion,    StringComparison.OrdinalIgnoreCase);

            // User variables: $variable:Name$
            result = VarPattern.Replace(result, m =>
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
