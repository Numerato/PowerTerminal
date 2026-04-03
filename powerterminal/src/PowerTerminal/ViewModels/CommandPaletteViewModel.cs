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
    public class CommandPaletteViewModel : ViewModelBase
    {
        private static readonly Regex VarPattern = new(@"\$([a-z0-9_]+)\$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly List<LinuxCommand> _allCommands;
        private readonly Dictionary<string, string> _systemVars;

        private string _searchQuery = string.Empty;
        private string _activeTag   = string.Empty;

        public CommandPaletteViewModel(ConfigService config, Dictionary<string, string> systemVars)
        {
            _allCommands = config.LoadAllCommands();
            _systemVars  = systemVars ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Build tag list from all loaded commands (sorted, unique, "All" first)
            var tags = _allCommands
                .SelectMany(c => c.Tags)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t)
                .ToList();
            Tags.Add("All");
            foreach (var t in tags) Tags.Add(t);

            ApplyFilter();
        }

        // ── Collections ──────────────────────────────────────────────────────

        public ObservableCollection<string>       Tags            { get; } = new();
        public ObservableCollection<LinuxCommand> FilteredCommands { get; } = new();

        // ── Properties ───────────────────────────────────────────────────────

        public string SearchQuery
        {
            get => _searchQuery;
            set { if (Set(ref _searchQuery, value)) ApplyFilter(); }
        }

        public string ActiveTag
        {
            get => _activeTag;
            set { if (Set(ref _activeTag, value)) ApplyFilter(); }
        }

        private LinuxCommand? _selectedCommand;
        public LinuxCommand? SelectedCommand
        {
            get => _selectedCommand;
            set => Set(ref _selectedCommand, value);
        }

        // ── Resolved command (system vars substituted, prompt vars left as-is) ──

        public string ResolvedCommand(LinuxCommand cmd)
        {
            return VarPattern.Replace(cmd.Command, m =>
            {
                var key = m.Groups[1].Value;
                return _systemVars.TryGetValue(key, out var val) ? val : m.Value;
            });
        }

        /// <summary>
        /// Returns a list of (text, kind) segments for rich rendering of a command.
        /// Kind: "text", "sys" (resolved system var), "prompt" (user-input var).
        /// </summary>
        public List<(string text, string kind)> GetCommandSegments(LinuxCommand cmd)
        {
            var segments = new List<(string, string)>();
            int pos = 0;
            foreach (Match m in VarPattern.Matches(cmd.Command))
            {
                if (m.Index > pos)
                    segments.Add((cmd.Command[pos..m.Index], "text"));

                var key = m.Groups[1].Value;
                if (_systemVars.TryGetValue(key, out var resolved))
                    segments.Add((resolved, "sys"));
                else
                    segments.Add((m.Value, "prompt"));

                pos = m.Index + m.Length;
            }
            if (pos < cmd.Command.Length)
                segments.Add((cmd.Command[pos..], "text"));
            return segments;
        }

        // ── Filtering ─────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            var q   = _searchQuery.Trim().ToLowerInvariant();
            var tag = _activeTag;

            var results = _allCommands.Where(cmd =>
            {
                bool tagOk = string.IsNullOrEmpty(tag) || tag == "All" ||
                             cmd.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

                if (!tagOk) return false;
                if (string.IsNullOrEmpty(q)) return true;

                return cmd.Title.Contains(q, StringComparison.OrdinalIgnoreCase)   ||
                       cmd.Command.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                       cmd.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                       cmd.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));
            });

            FilteredCommands.Clear();
            foreach (var cmd in results.OrderBy(c => c.Tags.FirstOrDefault()).ThenBy(c => c.Title))
                FilteredCommands.Add(cmd);

            // Keep selection valid
            if (SelectedCommand == null || !FilteredCommands.Contains(SelectedCommand))
                SelectedCommand = FilteredCommands.FirstOrDefault();
        }
    }
}
