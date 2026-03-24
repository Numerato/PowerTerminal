using System;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class PowerEditView : UserControl
    {
        private PowerEditViewModel Vm => DataContext as PowerEditViewModel;

        // Prevents re-entrancy when loading content from the ViewModel
        private bool _isLoadingContent;

        public PowerEditView()
        {
            InitializeComponent();
            EnsureSyntaxDefinitionsLoaded();

            // Dark-theme caret and selection colours (cannot be set in XAML for AvalonEdit)
            Editor.TextArea.Caret.CaretBrush = System.Windows.Media.Brushes.White;
            Editor.TextArea.SelectionBrush   = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(120, 92, 40, 0));
            Editor.TextArea.SelectionForeground = System.Windows.Media.Brushes.White;

            // Push document changes to the ViewModel
            Editor.Document.Changed += (_, _) =>
            {
                if (!_isLoadingContent && Vm != null)
                    Vm.Content = Editor.Document.Text;
            };

            // Update cursor position in status bar
            Editor.TextArea.Caret.PositionChanged += (_, _) =>
            {
                if (Vm == null) return;
                Vm.CursorLine = Editor.TextArea.Caret.Line;
                Vm.CursorCol  = Editor.TextArea.Caret.Column;
            };

            DataContextChanged += (_, _) => BindVmEvents();
        }

        // ── Syntax highlighting registration ───────────────────────────────

        private static bool _syntaxLoaded;

        private static void EnsureSyntaxDefinitionsLoaded()
        {
            if (_syntaxLoaded) return;
            _syntaxLoaded = true;
            // Custom definitions (not built-in to AvalonEdit)
            RegisterDefinition("YAML",       new[] { ".yaml", ".yml" },               "PowerTerminal.Resources.Syntax.yaml.xshd");
            RegisterDefinition("Bash",       new[] { ".sh", ".bash", ".zsh" },        "PowerTerminal.Resources.Syntax.bash.xshd");
            RegisterDefinition("INI",        new[] { ".ini", ".conf", ".cfg" },       "PowerTerminal.Resources.Syntax.ini.xshd");
            RegisterDefinition("TOML",       new[] { ".toml" },                        "PowerTerminal.Resources.Syntax.toml.xshd");
            RegisterDefinition("Dockerfile", new[] { ".dockerfile" },                  "PowerTerminal.Resources.Syntax.dockerfile.xshd");
            RegisterDefinition("Makefile",   new[] { ".mak", ".mk" },                 "PowerTerminal.Resources.Syntax.makefile.xshd");
            RegisterDefinition("Properties", new[] { ".properties" },                  "PowerTerminal.Resources.Syntax.properties.xshd");
            RegisterDefinition("Env",        new[] { ".env" },                         "PowerTerminal.Resources.Syntax.env.xshd");
        }

        private static void RegisterDefinition(string name, string[] extensions, string resourceName)
        {
            if (HighlightingManager.Instance.GetDefinition(name) != null) return;
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return;
            using var reader = new XmlTextReader(stream);
            var def = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(name, extensions, def);
        }

        private void ApplySyntaxHighlighting()
        {
            if (Vm == null) return;

            string filename = Path.GetFileName(Vm.FilePath).ToLowerInvariant();
            string ext      = Path.GetExtension(Vm.FilePath).ToLowerInvariant();

            // Filename-based detection for extension-less files
            string defName = filename switch
            {
                "dockerfile"                                              => "Dockerfile",
                "makefile" or "gnumakefile" or "bsdmakefile"             => "Makefile",
                ".bashrc" or ".bash_profile" or ".bash_aliases"
                    or ".zshrc" or ".zprofile" or ".profile"
                    or ".bash_logout"                                     => "Bash",
                ".env"                                                    => "Env",
                _                                                         => null
            };

            // Extension-based mapping (covers aliases not registered with HighlightingManager)
            if (defName == null)
            {
                defName = ext switch
                {
                    // aliases for built-in definitions
                    ".htm" or ".xhtml"                         => "HTML",
                    ".h" or ".hpp" or ".cc" or ".c" or ".hh"  => "C++",
                    ".ts" or ".tsx" or ".jsx" or ".mjs"        => "JavaScript",
                    ".scss" or ".less"                         => "CSS",
                    ".markdown"                                => "MarkDownWithFontSize",
                    ".pgsql" or ".tsql"                        => "TSQL",
                    ".psm1" or ".psd1" or ".psm"               => "PowerShell",
                    // aliases for our custom definitions
                    ".sh" or ".bash" or ".zsh"                 => "Bash",
                    ".conf" or ".cfg" or ".config"             => "INI",
                    ".env"                                     => "Env",
                    _                                          => null
                };
            }

            Editor.SyntaxHighlighting = defName != null
                ? HighlightingManager.Instance.GetDefinition(defName)
                : HighlightingManager.Instance.GetDefinitionByExtension(ext);
        }

        // ── ViewModel event wiring ──────────────────────────────────────────

        private PowerEditViewModel _lastVm;

        private void BindVmEvents()
        {
            if (_lastVm != null)
            {
                _lastVm.ContentLoaded       -= OnContentLoaded;
                _lastVm.BeforeSave          -= OnBeforeSave;
                _lastVm.FindNextRequested   -= OnFindNext;
                _lastVm.FindPrevRequested   -= OnFindPrev;
                _lastVm.ReplaceOneRequested -= OnReplaceOne;
                _lastVm.ReplaceAllRequested -= OnReplaceAll;
                _lastVm.GoToLineRequested   -= OnGoToLine;
                _lastVm.TimeDateRequested   -= OnTimeDate;
                _lastVm.FontChangeRequested -= OnFontChange;
                _lastVm.SaveAsRequested     -= OnSaveAs;
            }
            _lastVm = Vm;
            if (_lastVm == null) return;

            _lastVm.ContentLoaded       += OnContentLoaded;
            _lastVm.BeforeSave          += OnBeforeSave;
            _lastVm.FindNextRequested   += OnFindNext;
            _lastVm.FindPrevRequested   += OnFindPrev;
            _lastVm.ReplaceOneRequested += OnReplaceOne;
            _lastVm.ReplaceAllRequested += OnReplaceAll;
            _lastVm.GoToLineRequested   += OnGoToLine;
            _lastVm.TimeDateRequested   += OnTimeDate;
            _lastVm.FontChangeRequested += OnFontChange;
            _lastVm.SaveAsRequested     += OnSaveAs;
        }

        private void OnBeforeSave(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (Vm == null) return;
            var result = Services.SyntaxValidationService.Validate(Vm.Content, Vm.FilePath);
            if (result.IsValid) return;

            string location = result.Line > 0
                ? $" (line {result.Line}, col {result.Column})"
                : string.Empty;

            var answer = DarkMessageBox.Show(
                Window.GetWindow(this),
                $"Syntax error{location}:\n\n{result.ErrorMessage}\n\nSave anyway?",
                "Syntax Error",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                NavigateToError(result.Line, result.Column);
            }
        }

        private void NavigateToError(int line, int column)
        {
            if (line < 1) return;
            int targetLine = Math.Max(1, Math.Min(line, Editor.Document.LineCount));
            var docLine    = Editor.Document.GetLineByNumber(targetLine);
            int col        = column > 1 ? column - 1 : 0; // col is 1-based; convert to offset
            int offset     = Math.Min(docLine.Offset + col, docLine.EndOffset);
            Editor.Select(offset, 0);
            Editor.ScrollToLine(targetLine);
            Editor.Focus();
        }

        private void OnContentLoaded(object sender, string text)
        {
            _isLoadingContent = true;
            Editor.Document.Text = text;
            Editor.Document.UndoStack.ClearAll();
            _isLoadingContent = false;
            ApplySyntaxHighlighting();
        }

        // ── Keyboard shortcuts ─────────────────────────────────────────────

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (Vm == null) return;

            bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            if (ctrl && e.Key == Key.S) { _ = Vm.SaveAsync(); e.Handled = true; return; }
            if (ctrl && e.Key == Key.F) { Vm.ShowFindCommand.Execute(null); e.Handled = true; FocusSearch(); return; }
            if (ctrl && e.Key == Key.H) { Vm.ShowReplaceCommand.Execute(null); e.Handled = true; FocusSearch(); return; }
            if (ctrl && e.Key == Key.G) { Vm.GoToLineCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.F3)        { if (shift) OnFindPrev(null, null); else OnFindNext(null, null); e.Handled = true; return; }
            if (e.Key == Key.F5)        { Vm.TimeDateCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.Escape && Vm.ShowFind) { Vm.HideFindCommand.Execute(null); Editor.Focus(); e.Handled = true; }
        }

        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // AvalonEdit handles Ctrl+Z/Y natively via its undo stack
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OnFindNext(null, null); e.Handled = true; }
            if (e.Key == Key.Escape) { Vm?.HideFindCommand.Execute(null); Editor.Focus(); }
        }

        private void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OnReplaceOne(null, null); e.Handled = true; }
            if (e.Key == Key.Escape) { Vm?.HideFindCommand.Execute(null); Editor.Focus(); }
        }

        // ── Find / Replace ────────────────────────────────────────────────

        private void OnFindNext(object sender, EventArgs e)
        {
            if (Vm == null || string.IsNullOrEmpty(Vm.SearchText)) return;
            DoFind(forward: true);
        }

        private void OnFindPrev(object sender, EventArgs e)
        {
            if (Vm == null || string.IsNullOrEmpty(Vm.SearchText)) return;
            DoFind(forward: false);
        }

        private void DoFind(bool forward)
        {
            string text   = Editor.Document.Text;
            string search = Vm.SearchText;
            var comparison = Vm.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            int startFrom;
            if (forward)
                startFrom = Editor.SelectionStart + (Editor.SelectionLength > 0 ? Editor.SelectionLength : 1);
            else
                startFrom = Editor.SelectionStart - 1;

            int idx = -1;
            if (forward)
            {
                idx = text.IndexOf(search, Math.Max(0, startFrom), comparison);
                if (idx < 0)
                    idx = text.IndexOf(search, 0, comparison);
            }
            else
            {
                int from = Math.Max(0, Math.Min(startFrom, text.Length - 1));
                for (int i = from; i >= 0; i--)
                {
                    if (i + search.Length <= text.Length &&
                        string.Compare(text, i, search, 0, search.Length, comparison) == 0)
                    { idx = i; break; }
                }
                if (idx < 0)
                {
                    for (int i = text.Length - search.Length; i >= 0; i--)
                    {
                        if (string.Compare(text, i, search, 0, search.Length, comparison) == 0)
                        { idx = i; break; }
                    }
                }
            }

            if (idx >= 0)
            {
                Editor.Select(idx, search.Length);
                Editor.ScrollToLine(Editor.Document.GetLineByOffset(idx).LineNumber);
                Editor.Focus();
            }
        }

        private void OnReplaceOne(object sender, EventArgs e)
        {
            if (Vm == null || string.IsNullOrEmpty(Vm.SearchText) || Vm.IsReadOnly) return;
            var comparison = Vm.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (string.Equals(Editor.SelectedText, Vm.SearchText, comparison))
            {
                int start = Editor.SelectionStart;
                Editor.Document.Replace(start, Editor.SelectionLength, Vm.ReplaceText);
                Editor.Select(start, Vm.ReplaceText.Length);
            }
            DoFind(forward: true);
        }

        private void OnReplaceAll(object sender, EventArgs e)
        {
            if (Vm == null || string.IsNullOrEmpty(Vm.SearchText) || Vm.IsReadOnly) return;
            var comparison = Vm.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            string text  = Editor.Document.Text;
            string newText;
            int count = 0;

            if (comparison == StringComparison.Ordinal)
            {
                newText = text.Replace(Vm.SearchText, Vm.ReplaceText, StringComparison.Ordinal);
                int i = 0;
                while ((i = text.IndexOf(Vm.SearchText, i, StringComparison.Ordinal)) >= 0)
                { count++; i += Vm.SearchText.Length; }
            }
            else
            {
                newText = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    System.Text.RegularExpressions.Regex.Escape(Vm.SearchText),
                    Vm.ReplaceText.Replace("$", "$$"),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                count = System.Text.RegularExpressions.Regex.Matches(text,
                    System.Text.RegularExpressions.Regex.Escape(Vm.SearchText),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            }

            if (count > 0)
            {
                int pos = Editor.SelectionStart;
                Editor.Document.Text = newText;
                Editor.SelectionStart = Math.Min(pos, newText.Length);
            }
        }

        // ── Go To Line ────────────────────────────────────────────────────

        private void OnGoToLine(object sender, EventArgs e)
        {
            var win = new GoToLineWindow(Vm?.CursorLine ?? 1) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                int targetLine = Math.Max(1, Math.Min(win.LineNumber, Editor.Document.LineCount));
                var line = Editor.Document.GetLineByNumber(targetLine);
                Editor.Select(line.Offset, 0);
                Editor.ScrollToLine(targetLine);
                Editor.Focus();
            }
        }

        // ── Time/Date ─────────────────────────────────────────────────────

        private void OnTimeDate(object sender, EventArgs e)
        {
            if (Vm?.IsReadOnly == true) return;
            string stamp = DateTime.Now.ToString("HH:mm  dd/MM/yyyy");
            int start = Editor.SelectionStart;
            Editor.Document.Replace(start, Editor.SelectionLength, stamp);
            Editor.Select(start + stamp.Length, 0);
        }

        // ── Font change ───────────────────────────────────────────────────

        private void OnFontChange(object sender, EventArgs e)
        {
            var win = new FontPickerWindow(Vm?.FontFamily ?? "Consolas", Vm?.FontSize ?? 13)
            {
                Owner = Window.GetWindow(this)
            };
            if (win.ShowDialog() == true && Vm != null)
            {
                Vm.FontFamily = win.SelectedFamily;
                Vm.FontSize   = win.SelectedSize;
            }
        }

        // ── Save As ───────────────────────────────────────────────────────

        private async void OnSaveAs(object sender, EventArgs e)
        {
            if (Vm == null) return;
            var win = new VariablePromptWindow("Remote path to save as") { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true && !string.IsNullOrWhiteSpace(win.Value))
            {
                bool ok = await Vm.SaveToPathAsync(win.Value.Trim());
                if (!ok)
                    DarkMessageBox.Show(
                        Window.GetWindow(this),
                        "Save failed. Check the path and permissions.",
                        "PowerEdit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Focus helpers ─────────────────────────────────────────────────

        public void FocusEditor()
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                new Action(() =>
                {
                    Editor.Focus();
                    Keyboard.Focus(Editor);
                }));
        }

        private void FocusSearch()
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }
}