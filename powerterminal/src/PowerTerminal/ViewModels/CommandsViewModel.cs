using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.ViewModels
{
    /// <summary>Represents a single command pack (.json file) in the Commands side panel.</summary>
    public class CommandPackViewModel : ViewModelBase
    {
        public string FileName    { get; }
        public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(FileName);

        public ObservableCollection<LinuxCommand> Commands { get; } = new();

        public CommandPackViewModel(string fileName, IEnumerable<LinuxCommand> commands)
        {
            FileName = fileName;
            foreach (var cmd in commands)
                Commands.Add(cmd);
        }
    }

    /// <summary>ViewModel for the Commands editor side panel.</summary>
    public class CommandsViewModel : ViewModelBase
    {
        private readonly ConfigService _config;

        private CommandPackViewModel? _selectedPack;
        private LinuxCommand?         _selectedCommand;
        private bool                  _isEditFormVisible;
        private bool                  _isEditingExisting;
        private bool                  _isAddingPack;
        private string                _newPackName  = string.Empty;
        private string                _searchQuery  = string.Empty;

        // Edit form fields
        private string _editTitle       = string.Empty;
        private string _editCommand     = string.Empty;
        private string _editDescription = string.Empty;
        private string _editTags        = string.Empty;

        public CommandsViewModel(ConfigService config)
        {
            _config = config;

            NewPackCommand     = new RelayCommand(_ => BeginAddPack());
            ConfirmPackCommand = new RelayCommand(_ => ConfirmAddPack(),
                                                  _ => !string.IsNullOrWhiteSpace(NewPackName));
            CancelPackCommand  = new RelayCommand(_ => CancelAddPack());
            DeletePackCommand  = new RelayCommand(_ => DeleteSelectedPack(),
                                                  _ => SelectedPack != null);
            NewCommandCommand   = new RelayCommand(_ => BeginNewCommand(),
                                                   _ => SelectedPack != null);
            SaveCommandCommand  = new RelayCommand(_ => SaveCommand(),
                                                   _ => !string.IsNullOrWhiteSpace(EditTitle) && !string.IsNullOrWhiteSpace(EditCommand));
            DeleteCommandCommand = new RelayCommand(_ => DeleteSelectedCommand(),
                                                    _ => SelectedCommand != null && IsEditingExisting);
            CancelEditCommand   = new RelayCommand(_ => CancelEdit());
            ClearSearchCommand  = new RelayCommand(_ => SearchQuery = string.Empty);

            LoadPacks();
        }

        // ── Packs ─────────────────────────────────────────────────────────────

        public ObservableCollection<CommandPackViewModel> Packs { get; } = new();

        /// <summary>Commands in the selected pack that match the current search query.</summary>
        public ObservableCollection<LinuxCommand> FilteredCommands { get; } = new();

        public CommandPackViewModel? SelectedPack
        {
            get => _selectedPack;
            set
            {
                if (Set(ref _selectedPack, value))
                {
                    CancelEdit();
                    SelectedCommand = null;
                    ApplyFilter();
                }
            }
        }

        public bool IsAddingPack
        {
            get => _isAddingPack;
            private set => Set(ref _isAddingPack, value);
        }

        public string NewPackName
        {
            get => _newPackName;
            set => Set(ref _newPackName, value);
        }

        // ── Search ────────────────────────────────────────────────────────────

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (Set(ref _searchQuery, value))
                    ApplyFilter();
            }
        }

        // ── Commands list ─────────────────────────────────────────────────────

        public LinuxCommand? SelectedCommand
        {
            get => _selectedCommand;
            set
            {
                if (Set(ref _selectedCommand, value))
                {
                    if (value != null)
                        LoadCommandIntoForm(value);
                    else
                        ClearForm();
                }
            }
        }

        // ── Edit form ─────────────────────────────────────────────────────────

        public bool IsEditFormVisible
        {
            get => _isEditFormVisible;
            private set => Set(ref _isEditFormVisible, value);
        }

        /// <summary>True when editing an existing command; false when adding a new one.</summary>
        public bool IsEditingExisting
        {
            get => _isEditingExisting;
            private set => Set(ref _isEditingExisting, value);
        }

        public string EditTitle
        {
            get => _editTitle;
            set { Set(ref _editTitle, value); CommandManager.InvalidateRequerySuggested(); }
        }

        public string EditCommand
        {
            get => _editCommand;
            set { Set(ref _editCommand, value); CommandManager.InvalidateRequerySuggested(); }
        }

        public string EditDescription
        {
            get => _editDescription;
            set => Set(ref _editDescription, value);
        }

        public string EditTags
        {
            get => _editTags;
            set => Set(ref _editTags, value);
        }

        // ── Commands ──────────────────────────────────────────────────────────

        public ICommand NewPackCommand      { get; }
        public ICommand ConfirmPackCommand  { get; }
        public ICommand CancelPackCommand   { get; }
        public ICommand DeletePackCommand   { get; }
        public ICommand NewCommandCommand   { get; }
        public ICommand SaveCommandCommand  { get; }
        public ICommand DeleteCommandCommand{ get; }
        public ICommand CancelEditCommand   { get; }
        public ICommand ClearSearchCommand  { get; }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Pre-fills the edit form with <paramref name="commandText"/> from a terminal selection
        /// and selects the <c>personal.json</c> pack as the target.
        /// </summary>
        public void StartNewFromSelection(string commandText)
        {
            // Pick personal.json if available, otherwise first pack
            var personal = Packs.FirstOrDefault(p =>
                string.Equals(p.FileName, "personal.json", StringComparison.OrdinalIgnoreCase))
                ?? Packs.FirstOrDefault();
            if (personal != null)
                SelectedPack = personal;

            SelectedCommand   = null;
            IsEditingExisting = false;
            IsEditFormVisible = true;
            EditTitle         = string.Empty;
            EditCommand       = commandText.Trim();
            EditDescription   = string.Empty;
            EditTags          = string.Empty;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void LoadPacks()
        {
            Packs.Clear();
            foreach (var (fileName, commands) in _config.LoadCommandPacks())
                Packs.Add(new CommandPackViewModel(fileName, commands));

            SelectedPack = Packs.FirstOrDefault(p =>
                string.Equals(p.FileName, "personal.json", StringComparison.OrdinalIgnoreCase))
                ?? Packs.FirstOrDefault();
            // SelectedPack setter calls ApplyFilter; also call here for the null case
            if (SelectedPack == null) ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredCommands.Clear();
            if (SelectedPack == null) return;

            var q = _searchQuery.Trim();
            var source = string.IsNullOrEmpty(q)
                ? SelectedPack.Commands
                : SelectedPack.Commands.Where(c =>
                    c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    c.Command.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    c.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    c.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));

            foreach (var cmd in source)
                FilteredCommands.Add(cmd);
        }

        private void LoadCommandIntoForm(LinuxCommand cmd)
        {
            IsEditingExisting = true;
            IsEditFormVisible = true;
            EditTitle         = cmd.Title;
            EditCommand       = cmd.Command;
            EditDescription   = cmd.Description;
            EditTags          = string.Join(", ", cmd.Tags);
        }

        private void ClearForm()
        {
            EditTitle       = string.Empty;
            EditCommand     = string.Empty;
            EditDescription = string.Empty;
            EditTags        = string.Empty;
        }

        private void BeginNewCommand()
        {
            SelectedCommand   = null;
            IsEditingExisting = false;
            IsEditFormVisible = true;
            ClearForm();
        }

        private void SaveCommand()
        {
            if (SelectedPack == null) return;
            if (string.IsNullOrWhiteSpace(EditTitle) || string.IsNullOrWhiteSpace(EditCommand))
                return;

            var tags = EditTags
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (IsEditingExisting && SelectedCommand != null)
            {
                // Update existing
                SelectedCommand.Title       = EditTitle;
                SelectedCommand.Command     = EditCommand;
                SelectedCommand.Description = EditDescription;
                SelectedCommand.Tags        = tags;
            }
            else
            {
                // Add new
                var cmd = new LinuxCommand
                {
                    Title       = EditTitle,
                    Command     = EditCommand,
                    Description = EditDescription,
                    Tags        = tags
                };
                SelectedPack.Commands.Add(cmd);
                SelectedCommand   = cmd;
                IsEditingExisting = true;
            }

            PersistSelectedPack();
            ApplyFilter();
        }

        private void DeleteSelectedCommand()
        {
            if (SelectedPack == null || SelectedCommand == null) return;
            SelectedPack.Commands.Remove(SelectedCommand);
            SelectedCommand   = null;
            IsEditFormVisible = false;
            PersistSelectedPack();
            ApplyFilter();
        }

        private void CancelEdit()
        {
            IsEditFormVisible = false;
            IsEditingExisting = false;
            SelectedCommand   = null;
            ClearForm();
        }

        private void BeginAddPack()
        {
            NewPackName  = string.Empty;
            IsAddingPack = true;
        }

        private void ConfirmAddPack()
        {
            if (string.IsNullOrWhiteSpace(NewPackName)) return;
            string fileName = _config.CreateCommandPack(NewPackName.Trim());
            var packVm = new CommandPackViewModel(fileName, Enumerable.Empty<LinuxCommand>());
            Packs.Add(packVm);
            SelectedPack = packVm;
            IsAddingPack = false;
            NewPackName  = string.Empty;
        }

        private void CancelAddPack()
        {
            IsAddingPack = false;
            NewPackName  = string.Empty;
        }

        public void DeleteSelectedPack()
        {
            if (SelectedPack == null) return;
            _config.DeleteCommandPack(SelectedPack.FileName);
            int idx = Packs.IndexOf(SelectedPack);
            Packs.Remove(SelectedPack);
            SelectedPack = Packs.Count > 0 ? Packs[Math.Max(0, idx - 1)] : null;
            IsEditFormVisible = false;
        }

        private void PersistSelectedPack()
        {
            if (SelectedPack == null) return;
            _config.SaveCommandPack(SelectedPack.FileName, SelectedPack.Commands);
        }
    }
}
