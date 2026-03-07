using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PowerTerminal.Models;
using PowerTerminal.Services;

namespace PowerTerminal.ViewModels
{
    public class WikiEditorViewModel : ViewModelBase
    {
        private readonly WikiService _wiki;
        private WikiEntry _entry = new();
        private WikiSection? _selectedSection;

        public WikiEditorViewModel(WikiService wiki)
        {
            _wiki = wiki;

            AddTextSectionCommand    = new RelayCommand(_ => AddSection(WikiSectionType.Text));
            AddCommandSectionCommand = new RelayCommand(_ => AddSection(WikiSectionType.Command));
            RemoveSectionCommand     = new RelayCommand(_ => RemoveSelectedSection(), _ => SelectedSection != null);
            MoveSectionUpCommand     = new RelayCommand(_ => MoveSection(-1), _ => CanMoveUp());
            MoveSectionDownCommand   = new RelayCommand(_ => MoveSection(1),  _ => CanMoveDown());
            SaveCommand              = new RelayCommand(_ => Save());
            CancelCommand            = new RelayCommand(_ => Cancel());
        }

        public ICommand AddTextSectionCommand    { get; }
        public ICommand AddCommandSectionCommand { get; }
        public ICommand RemoveSectionCommand     { get; }
        public ICommand MoveSectionUpCommand     { get; }
        public ICommand MoveSectionDownCommand   { get; }
        public ICommand SaveCommand              { get; }
        public ICommand CancelCommand            { get; }

        public ObservableCollection<WikiSection> Sections { get; } = new();

        public WikiEntry Entry
        {
            get => _entry;
            set
            {
                _entry = value;
                Sections.Clear();
                foreach (var s in value.Sections) Sections.Add(s);
                Tags = string.Join(", ", value.Tags);
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(Tags));
            }
        }

        public string Title
        {
            get => _entry.Title;
            set { _entry.Title = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _entry.Description;
            set { _entry.Description = value; OnPropertyChanged(); }
        }

        private string _tags = string.Empty;
        public string Tags
        {
            get => _tags;
            set => Set(ref _tags, value);
        }

        public WikiSection? SelectedSection
        {
            get => _selectedSection;
            set => Set(ref _selectedSection, value);
        }

        public event Action? SaveRequested;
        public event Action? CancelRequested;

        public void LoadNew()
        {
            Entry = new WikiEntry();
        }

        public void LoadExisting(WikiEntry existing)
        {
            Entry = new WikiEntry
            {
                Id          = existing.Id,
                Title       = existing.Title,
                Description = existing.Description,
                Tags        = new System.Collections.Generic.List<string>(existing.Tags),
                Sections    = new System.Collections.Generic.List<WikiSection>(existing.Sections),
                CreatedAt   = existing.CreatedAt,
                FileName    = existing.FileName
            };
        }

        private void AddSection(WikiSectionType type)
        {
            var section = new WikiSection { Type = type, Language = type == WikiSectionType.Command ? "bash" : null };
            Sections.Add(section);
            SelectedSection = section;
        }

        private void RemoveSelectedSection()
        {
            if (SelectedSection != null)
                Sections.Remove(SelectedSection);
        }

        private void MoveSection(int direction)
        {
            if (SelectedSection == null) return;
            int idx = Sections.IndexOf(SelectedSection);
            int newIdx = idx + direction;
            if (newIdx >= 0 && newIdx < Sections.Count)
            {
                Sections.RemoveAt(idx);
                Sections.Insert(newIdx, SelectedSection);
            }
        }

        private bool CanMoveUp()
        {
            return SelectedSection != null && Sections.IndexOf(SelectedSection) > 0;
        }

        private bool CanMoveDown()
        {
            return SelectedSection != null && Sections.IndexOf(SelectedSection) < Sections.Count - 1;
        }

        private void Save()
        {
            _entry.Title       = Title;
            _entry.Description = Description;
            _entry.Tags        = Tags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
            _entry.Sections = new System.Collections.Generic.List<WikiSection>(Sections);
            _wiki.Save(_entry);
            SaveRequested?.Invoke();
        }

        private void Cancel()
        {
            CancelRequested?.Invoke();
        }
    }
}
