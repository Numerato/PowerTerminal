using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PowerTerminal.ViewModels
{
    /// <summary>ViewModel for the embedded PowerEdit notepad-style editor.</summary>
    public class PowerEditViewModel : ViewModelBase
    {
        private string _filePath     = string.Empty;
        private string _content      = string.Empty;
        private bool   _isReadOnly;
        private bool   _isModified;
        private bool   _useSudo;
        private string _sudoPassword = string.Empty;
        private bool   _wordWrap;
        private bool   _showStatusBar = true;
        private int    _cursorLine    = 1;
        private int    _cursorCol     = 1;
        private string _fontFamily    = "Consolas";
        private double _fontSize      = 13;
        private bool   _showFind;
        private bool   _showReplace;
        private string _searchText  = string.Empty;
        private string _replaceText = string.Empty;
        private bool   _matchCase;

        // Injected SSH callbacks — set by TerminalTabView.xaml.cs after construction.
        public Func<string, string, Task<(string, bool)>> ReadFile  { get; set; }
        public Func<string, string, string, Task<bool>>   WriteFile { get; set; }

        // Events raised to the code-behind (UI actions that need direct TextBox access)
        public event EventHandler CloseRequested;
        public event EventHandler SaveAsRequested;
        public event EventHandler GoToLineRequested;
        public event EventHandler FontChangeRequested;
        public event EventHandler TimeDateRequested;
        public event EventHandler FindNextRequested;
        public event EventHandler FindPrevRequested;
        public event EventHandler ReplaceOneRequested;
        public event EventHandler ReplaceAllRequested;

        public PowerEditViewModel()
        {
            SaveCommand          = new RelayCommand(async _ => await SaveAsync(),    _ => !IsReadOnly);
            SaveAsCommand        = new RelayCommand(_ => SaveAsRequested?.Invoke(this, EventArgs.Empty));
            CloseCommand         = new RelayCommand(_ => CloseRequested?.Invoke(this, EventArgs.Empty));
            ShowFindCommand      = new RelayCommand(_ => { ShowFind = true; ShowReplace = false; });
            ShowReplaceCommand   = new RelayCommand(_ => { ShowFind = true; ShowReplace = true; }, _ => !IsReadOnly);
            HideFindCommand      = new RelayCommand(_ => { ShowFind = false; ShowReplace = false; });
            FindNextCommand      = new RelayCommand(_ => FindNextRequested?.Invoke(this, EventArgs.Empty));
            FindPrevCommand      = new RelayCommand(_ => FindPrevRequested?.Invoke(this, EventArgs.Empty));
            ReplaceOneCommand    = new RelayCommand(_ => ReplaceOneRequested?.Invoke(this, EventArgs.Empty), _ => !IsReadOnly);
            ReplaceAllCommand    = new RelayCommand(_ => ReplaceAllRequested?.Invoke(this, EventArgs.Empty), _ => !IsReadOnly);
            GoToLineCommand      = new RelayCommand(_ => GoToLineRequested?.Invoke(this, EventArgs.Empty));
            TimeDateCommand      = new RelayCommand(_ => TimeDateRequested?.Invoke(this, EventArgs.Empty), _ => !IsReadOnly);
            FontCommand          = new RelayCommand(_ => FontChangeRequested?.Invoke(this, EventArgs.Empty));
            WordWrapCommand      = new RelayCommand(_ => WordWrap = !WordWrap);
            ShowStatusBarCommand = new RelayCommand(_ => ShowStatusBar = !ShowStatusBar);
        }

        // ── Properties ───────────────────────────────────────────────────────

        public string FilePath
        {
            get => _filePath;
            set { Set(ref _filePath, value); OnPropertyChanged(nameof(TitleText)); }
        }

        /// <summary>Raw text content of the file. Setting this marks IsModified.</summary>
        public string Content
        {
            get => _content;
            set
            {
                if (Set(ref _content, value))
                {
                    IsModified = true;
                    OnPropertyChanged(nameof(TitleText));
                }
            }
        }

        public bool IsReadOnly
        {
            get => _isReadOnly;
            set { Set(ref _isReadOnly, value); OnPropertyChanged(nameof(StatusText)); }
        }

        public bool IsModified
        {
            get => _isModified;
            set { Set(ref _isModified, value); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(TitleText)); }
        }

        public bool   UseSudo      { get => _useSudo;      set { Set(ref _useSudo, value);      OnPropertyChanged(nameof(StatusText)); } }
        public string SudoPassword { get => _sudoPassword; set => Set(ref _sudoPassword, value); }

        public bool WordWrap
        {
            get => _wordWrap;
            set
            {
                Set(ref _wordWrap, value);
                OnPropertyChanged(nameof(TextWrappingMode));
                OnPropertyChanged(nameof(HScrollVisibility));
            }
        }

        public bool   ShowStatusBar { get => _showStatusBar; set => Set(ref _showStatusBar, value); }
        public bool   ShowFind      { get => _showFind;      set => Set(ref _showFind, value); }
        public bool   ShowReplace   { get => _showReplace;   set => Set(ref _showReplace, value); }
        public string SearchText    { get => _searchText;    set => Set(ref _searchText, value); }
        public string ReplaceText   { get => _replaceText;   set => Set(ref _replaceText, value); }
        public bool   MatchCase     { get => _matchCase;     set => Set(ref _matchCase, value); }
        public string FontFamily    { get => _fontFamily;    set => Set(ref _fontFamily, value); }
        public double FontSize      { get => _fontSize;      set => Set(ref _fontSize, value); }

        public int CursorLine
        {
            get => _cursorLine;
            set { Set(ref _cursorLine, value); OnPropertyChanged(nameof(StatusText)); }
        }

        public int CursorCol
        {
            get => _cursorCol;
            set { Set(ref _cursorCol, value); OnPropertyChanged(nameof(StatusText)); }
        }

        // Derived display properties
        public TextWrapping       TextWrappingMode  => _wordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        public ScrollBarVisibility HScrollVisibility => _wordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

        public string TitleText  => System.IO.Path.GetFileName(FilePath) + (IsModified ? " *" : "");

        public string StatusText
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>
                {
                    $"Ln {CursorLine}, Col {CursorCol}"
                };
                if (IsModified) parts.Add("Modified");
                if (IsReadOnly) parts.Add("Read-only");
                if (UseSudo)    parts.Add("sudo");
                return string.Join("  │  ", parts);
            }
        }

        // ── Commands ─────────────────────────────────────────────────────────

        public RelayCommand SaveCommand          { get; }
        public RelayCommand SaveAsCommand        { get; }
        public RelayCommand CloseCommand         { get; }
        public RelayCommand ShowFindCommand      { get; }
        public RelayCommand ShowReplaceCommand   { get; }
        public RelayCommand HideFindCommand      { get; }
        public RelayCommand FindNextCommand      { get; }
        public RelayCommand FindPrevCommand      { get; }
        public RelayCommand ReplaceOneCommand    { get; }
        public RelayCommand ReplaceAllCommand    { get; }
        public RelayCommand GoToLineCommand      { get; }
        public RelayCommand TimeDateCommand      { get; }
        public RelayCommand FontCommand          { get; }
        public RelayCommand WordWrapCommand      { get; }
        public RelayCommand ShowStatusBarCommand { get; }

        // ── Methods ──────────────────────────────────────────────────────────

        /// <summary>Load content without marking the file as modified.</summary>
        public void LoadContent(string text)
        {
            _content = text;
            OnPropertyChanged(nameof(Content));
            IsModified = false;
            OnPropertyChanged(nameof(TitleText));
        }

        public async Task<bool> SaveAsync()
        {
            if (IsReadOnly || WriteFile == null) return false;
            bool ok = await WriteFile(FilePath, _content, UseSudo ? SudoPassword : string.Empty);
            if (ok) IsModified = false;
            return ok;
        }

        public async Task<bool> SaveToPathAsync(string newPath)
        {
            if (WriteFile == null) return false;
            bool ok = await WriteFile(newPath, _content, UseSudo ? SudoPassword : string.Empty);
            if (ok)
            {
                FilePath   = newPath;
                IsReadOnly = false;   // saved successfully to a writable path — no longer read-only
                IsModified = false;
            }
            return ok;
        }
    }
}
