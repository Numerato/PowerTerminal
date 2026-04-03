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
    public class VariablesViewModel : ViewModelBase
    {
        // Matches shell error strings that should be treated as "no value".
        // Anchored at the start so legitimate values containing these words mid-string
        // (e.g. a hostname like "syntax-server") are not suppressed.
        private static readonly Regex BadValuePattern = new(
            @"^\s*(bad\s+command|syntax\s+error|command\s+not\s+found|not\s+found|" +
            @"permission\s+denied|bash:|zsh:|fish:|/bin/sh:|cannot\s+open|no\s+such\s+file)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Returns <paramref name="value"/> unchanged, or empty string when it
        /// looks like a shell error message.</summary>
        private static string Filter(string value)
            => string.IsNullOrWhiteSpace(value) || BadValuePattern.IsMatch(value)
                ? string.Empty
                : value.Trim();
        private readonly ConfigService _config;
        private string _newVariableName  = string.Empty;
        private string _newVariableValue = string.Empty;
        private TerminalTabViewModel? _activeTerminal;

        public VariablesViewModel(ConfigService config)
        {
            _config = config;
            LoadCustomVariables();

            AddCommand     = new RelayCommand(_ => AddVariable(), _ => CanAddVariable());
            DeleteCommand  = new RelayCommand(p => DeleteVariable(p as CustomVariable));
            SaveCommand    = new RelayCommand(_ => SaveCustomVariables());
            RefreshCommand = new RelayCommand(_ => RefreshSystemVariables(_activeTerminal));
        }

        public ObservableCollection<VariableItem>   SystemVariables { get; } = new();
        public ObservableCollection<CustomVariable> CustomVariables { get; } = new();

        public string NewVariableName
        {
            get => _newVariableName;
            set
            {
                if (Set(ref _newVariableName, value))
                    OnPropertyChanged(nameof(IsNewNameDuplicate));
            }
        }

        public string NewVariableValue
        {
            get => _newVariableValue;
            set => Set(ref _newVariableValue, value);
        }

        /// <summary>True if the new variable name would be a duplicate.</summary>
        public bool IsNewNameDuplicate =>
            !string.IsNullOrWhiteSpace(NewVariableName) && IsDuplicateName(NewVariableName);

        public ICommand AddCommand     { get; }
        public ICommand DeleteCommand  { get; }
        public ICommand SaveCommand    { get; }
        public ICommand RefreshCommand { get; }

        /// <summary>Refreshes the system-variable list from the currently active terminal tab.</summary>
        public void RefreshSystemVariables(TerminalTabViewModel? tab)
        {
            _activeTerminal = tab;
            SystemVariables.Clear();
            if (tab == null) return;

            void Add(string name, string raw)
            {
                var value = Filter(raw);
                if (!string.IsNullOrEmpty(value))
                    SystemVariables.Add(new VariableItem { Name = name, Value = value });
            }

            Add("$currentdirectory$", tab.CurrentDirectory);
            Add("$operatingsystem$",  tab.OperatingSystem);
            Add("$version$",          tab.OsVersion);
            Add("$homefolder$",       tab.HomeFolder);
            Add("$hardware$",         tab.Hardware);
            Add("$disksizes$",        tab.DiskSizes);
            Add("$ipaddress$",        tab.IpAddress);
            Add("$hostname$",         tab.Hostname);
            Add("$cpu$",              tab.CpuInfo);
            Add("$memory$",           tab.TotalMemory);
            Add("$username$",         tab.Username);
            Add("$kernelversion$",    tab.KernelVersion);
            Add("$defaultshell$",     tab.DefaultShell);
            Add("$timezone$",         tab.Timezone);
            Add("$cpucount$",         tab.CpuCount);
            Add("$freememory$",       tab.FreeMemory);
            Add("$freedisk$",         tab.FreeDisk);
            Add("$publicip$",         tab.PublicIp);
            Add("$sudouser$",         tab.SudoUser);
        }

        private void LoadCustomVariables()
        {
            var vars = _config.LoadCustomVariables();
            CustomVariables.Clear();

            // Remove duplicates: keep first occurrence, remove subsequent ones
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uniqueVars = new List<CustomVariable>();
            foreach (var v in vars.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(v.Name))
                {
                    var cv = new CustomVariable { Name = v.Name, Value = v.Value };
                    WireVariable(cv);
                    uniqueVars.Add(cv);
                }
            }

            foreach (var cv in uniqueVars)
                CustomVariables.Add(cv);

            // Save if duplicates were removed
            if (uniqueVars.Count < vars.Count)
                SaveCustomVariables();
        }

        /// <summary>Subscribe to property changes so any inline edit auto-saves and duplicates are rechecked.</summary>
        private void WireVariable(CustomVariable v)
        {
            v.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CustomVariable.Name))
                    UpdateDuplicateFlags();
                if (args.PropertyName != nameof(CustomVariable.IsDuplicate))
                    SaveCustomVariables();
            };
        }

        /// <summary>Updates the IsDuplicate flag on all custom variables.</summary>
        private void UpdateDuplicateFlags()
        {
            var nameCounts = CustomVariables
                .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var v in CustomVariables)
                v.IsDuplicate = nameCounts.TryGetValue(v.Name, out int count) && count > 1;
        }

        /// <summary>Normalizes the variable name to $name$ format.</summary>
        private static string NormalizeName(string name)
        {
            name = name.Trim();
            if (!name.StartsWith("$")) name = "$" + name;
            if (!name.EndsWith("$"))   name = name + "$";
            return name;
        }

        /// <summary>Checks if a variable name already exists (case-insensitive).</summary>
        public bool IsDuplicateName(string name, CustomVariable? excludeVariable = null)
        {
            string normalized = NormalizeName(name);
            return CustomVariables.Any(v =>
                v != excludeVariable &&
                string.Equals(v.Name, normalized, StringComparison.OrdinalIgnoreCase));
        }

        private bool CanAddVariable()
        {
            if (string.IsNullOrWhiteSpace(NewVariableName))
                return false;

            // Check for duplicates
            return !IsDuplicateName(NewVariableName);
        }

        private void AddVariable()
        {
            string name = NormalizeName(NewVariableName);

            // Double-check for duplicates (in case CanExecute wasn't re-evaluated)
            if (IsDuplicateName(name))
                return;

            var cv = new CustomVariable { Name = name, Value = NewVariableValue.Trim() };
            WireVariable(cv);

            // Insert in sorted order
            int insertIndex = 0;
            for (int i = 0; i < CustomVariables.Count; i++)
            {
                if (string.Compare(CustomVariables[i].Name, name, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    insertIndex = i;
                    break;
                }
                insertIndex = i + 1;
            }
            CustomVariables.Insert(insertIndex, cv);

            NewVariableName  = string.Empty;
            NewVariableValue = string.Empty;
            SaveCustomVariables();
        }

        private void DeleteVariable(CustomVariable? variable)
        {
            if (variable == null) return;
            CustomVariables.Remove(variable);
            SaveCustomVariables();
        }

        /// <summary>Re-sorts the custom variables alphabetically after an edit.</summary>
        public void ResortCustomVariables()
        {
            var sorted = CustomVariables.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
            CustomVariables.Clear();
            foreach (var v in sorted)
                CustomVariables.Add(v);
        }

        public void SaveCustomVariables()
        {
            var list = new List<CustomVariable>();
            foreach (var v in CustomVariables)
                list.Add(new CustomVariable { Name = v.Name, Value = v.Value });
            _config.SaveCustomVariables(list);
        }
    }
}



