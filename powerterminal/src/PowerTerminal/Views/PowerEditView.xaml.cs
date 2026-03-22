using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerTerminal.ViewModels;

namespace PowerTerminal.Views
{
    public partial class PowerEditView : UserControl
    {
        private PowerEditViewModel Vm => DataContext as PowerEditViewModel;

        public PowerEditView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => BindVmEvents();
        }

        // ── ViewModel event wiring ──────────────────────────────────────────

        private PowerEditViewModel _lastVm;

        private void BindVmEvents()
        {
            if (_lastVm != null)
            {
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

            _lastVm.FindNextRequested   += OnFindNext;
            _lastVm.FindPrevRequested   += OnFindPrev;
            _lastVm.ReplaceOneRequested += OnReplaceOne;
            _lastVm.ReplaceAllRequested += OnReplaceAll;
            _lastVm.GoToLineRequested   += OnGoToLine;
            _lastVm.TimeDateRequested   += OnTimeDate;
            _lastVm.FontChangeRequested += OnFontChange;
            _lastVm.SaveAsRequested     += OnSaveAs;
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
            // Let Ctrl+Z/Y pass through to the TextBox's built-in undo/redo
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

        // ── Status bar — cursor position ──────────────────────────────────

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (Vm == null) return;
            int idx = Editor.SelectionStart;
            string text = Editor.Text ?? string.Empty;
            int line = 1, col = 1;
            for (int i = 0; i < idx && i < text.Length; i++)
            {
                if (text[i] == '\n') { line++; col = 1; }
                else col++;
            }
            Vm.CursorLine = line;
            Vm.CursorCol  = col;
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
            string text   = Editor.Text ?? string.Empty;
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
                if (idx < 0) // wrap around
                    idx = text.IndexOf(search, 0, comparison);
            }
            else
            {
                int from = Math.Max(0, Math.Min(startFrom, text.Length - 1));
                // Search backwards from 'from'
                for (int i = from; i >= 0; i--)
                {
                    if (i + search.Length <= text.Length)
                    {
                        if (string.Compare(text, i, search, 0, search.Length, comparison) == 0)
                        {
                            idx = i;
                            break;
                        }
                    }
                }
                if (idx < 0) // wrap around to end
                {
                    for (int i = text.Length - search.Length; i >= 0; i--)
                    {
                        if (string.Compare(text, i, search, 0, search.Length, comparison) == 0)
                        {
                            idx = i;
                            break;
                        }
                    }
                }
            }

            if (idx >= 0)
            {
                Editor.Select(idx, search.Length);
                Editor.ScrollToLine(GetLineNumber(text, idx));
                Editor.Focus();
            }
        }

        private void OnReplaceOne(object sender, EventArgs e)
        {
            if (Vm == null || string.IsNullOrEmpty(Vm.SearchText) || Vm.IsReadOnly) return;
            var comparison = Vm.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            string selected = Editor.SelectedText;
            if (string.Equals(selected, Vm.SearchText, comparison))
            {
                // Replace current selection
                int start = Editor.SelectionStart;
                Editor.SelectedText = Vm.ReplaceText;
                Editor.Select(start, Vm.ReplaceText.Length);
            }
            // Move to next occurrence
            DoFind(forward: true);
        }

        private void OnReplaceAll(object sender, EventArgs e)
        {
            if (Vm == null || string.IsNullOrEmpty(Vm.SearchText) || Vm.IsReadOnly) return;
            var comparison = Vm.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            string text  = Editor.Text ?? string.Empty;
            string newText;
            int count = 0;

            if (comparison == StringComparison.Ordinal)
            {
                newText = text.Replace(Vm.SearchText, Vm.ReplaceText, StringComparison.Ordinal);
                // Count occurrences
                int idx = 0;
                while ((idx = text.IndexOf(Vm.SearchText, idx, StringComparison.Ordinal)) >= 0)
                { count++; idx += Vm.SearchText.Length; }
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
                Editor.Text = newText;
                Editor.SelectionStart = Math.Min(pos, newText.Length);
            }
        }

        // ── Go To Line ────────────────────────────────────────────────────

        private void OnGoToLine(object sender, EventArgs e)
        {
            var win = new GoToLineWindow(Vm?.CursorLine ?? 1) { Owner = Window.GetWindow(this) };
            if (win.ShowDialog() == true)
            {
                int targetLine = win.LineNumber;
                string text = Editor.Text ?? string.Empty;
                int idx = GetIndexOfLine(text, targetLine);
                Editor.SelectionStart  = idx;
                Editor.SelectionLength = 0;
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
            Editor.SelectedText = stamp;
            Editor.SelectionStart  = start + stamp.Length;
            Editor.SelectionLength = 0;
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
            // Defer focus until after the layout pass so the newly-visible
            // editor has been measured and is ready to accept keyboard focus.
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

        // ── Utility ───────────────────────────────────────────────────────

        private static int GetLineNumber(string text, int charIndex)
        {
            int line = 1;
            for (int i = 0; i < charIndex && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private static int GetIndexOfLine(string text, int lineNumber)
        {
            int line = 1, i = 0;
            while (i < text.Length && line < lineNumber)
            {
                if (text[i] == '\n') line++;
                i++;
            }
            return i;
        }
    }
}
