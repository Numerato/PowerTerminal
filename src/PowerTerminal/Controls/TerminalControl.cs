#nullable enable
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Text.RegularExpressions;

namespace PowerTerminal.Controls
{
    /// <summary>
    /// A simple VT100/ANSI terminal emulator control rendered in a WPF RichTextBox.
    /// Supports color attributes, basic cursor movement, and screen scrollback.
    /// </summary>
    public class TerminalControl : TextEditor
    {
        // ── Dependency Properties ─────────────────────────────────────────────
        public static readonly DependencyProperty TerminalFontFamilyProperty =
            DependencyProperty.Register(nameof(TerminalFontFamily), typeof(FontFamily),
                typeof(TerminalControl), new PropertyMetadata(new FontFamily("Cascadia Code, Consolas, Courier New"),
                    (d, _) => ((TerminalControl)d).FontFamily = (FontFamily)d.GetValue(TerminalFontFamilyProperty)));

        public static readonly DependencyProperty TerminalFontSizeProperty =
            DependencyProperty.Register(nameof(TerminalFontSize), typeof(double),
                typeof(TerminalControl), new PropertyMetadata(13.0,
                    (d, _) => ((TerminalControl)d).FontSize = (double)d.GetValue(TerminalFontSizeProperty)));

        public FontFamily TerminalFontFamily
        {
            get => (FontFamily)GetValue(TerminalFontFamilyProperty);
            set => SetValue(TerminalFontFamilyProperty, value);
        }

        public double TerminalFontSize
        {
            get => (double)GetValue(TerminalFontSizeProperty);
            set => SetValue(TerminalFontSizeProperty, value);
        }

        private readonly TextSegmentCollection<ColorSegment> _segments;
        private readonly AnsiColorizer _colorizer;
        private TerminalStyle _currentStyle = new();

        public event Action<string>? UserInput;

        // Queue for background appending to avoid UI freeze
        private readonly ConcurrentQueue<string> _incomingDataQueue = new();
        private string _leftoverStr = "";
        private int _isRendering;

        private static readonly SolidColorBrush TerminalForeground = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        private static readonly SolidColorBrush TerminalBackground = new SolidColorBrush(Color.FromRgb(12, 12, 12));
        private static readonly SolidColorBrush TerminalCaret      = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        private static readonly SolidColorBrush TerminalSelection  = new SolidColorBrush(Color.FromArgb(120, 92, 40, 0));

        static TerminalControl()
        {
            TerminalForeground.Freeze();
            TerminalBackground.Freeze();
            TerminalCaret.Freeze();
            TerminalSelection.Freeze();
            foreach (var b in AnsiColors) b.Freeze();
        }

        public TerminalControl()
        {
            IsReadOnly = true;
            ShowLineNumbers = false;
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New");
            FontSize = 13;
            Background = TerminalBackground;
            Foreground = TerminalForeground;

            // Critical for performance with large outputs
            Options.EnableTextDragDrop = false;
            Options.EnableRectangularSelection = false;
            Options.EnableHyperlinks = false;
            Options.AllowScrollBelowDocument = false;

            _segments = new TextSegmentCollection<ColorSegment>(this.Document);
            _colorizer = new AnsiColorizer(_segments);
            TextArea.TextView.LineTransformers.Add(_colorizer);

            // Apply colors to the TextArea immediately (template may already be applied)
            ApplyTerminalColors();

            PreviewKeyDown += OnPreviewKeyDown;
            PreviewTextInput += OnPreviewTextInput;

            Loaded += (_, _) => ApplyTerminalColors();

            // Default focus
            Focusable = true;

            // Context Menu
            ContextMenu = new ContextMenu();
            var copy = new MenuItem { Header = "Copy" };
            copy.Click += (s, e) => Copy();
            ContextMenu.Items.Add(copy);

            var paste = new MenuItem { Header = "Paste" };
            paste.Click += (s, e) => Paste();
            ContextMenu.Items.Add(paste);

            ContextMenu.Opened += (s, e) =>
            {
                copy.IsEnabled = !TextArea.Selection.IsEmpty;
                paste.IsEnabled = Clipboard.ContainsText();
            };
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            // Re-apply after the control template is fully instantiated, so
            // TextArea.Foreground / caret / selection are definitely set.
            ApplyTerminalColors();
        }

        private void ApplyTerminalColors()
        {
            // AvalonEdit renders text through TextArea → TextView, not through
            // the outer TextEditor's Foreground property.  We must set the colors
            // explicitly on the TextArea so the DrawingContext picks them up.
            TextArea.Foreground        = TerminalForeground;
            TextArea.Background        = TerminalBackground;
            TextArea.TextView.NonPrintableCharacterBrush = TerminalForeground;

            // Caret
            TextArea.Caret.CaretBrush = TerminalCaret;

            // Selection highlight
            TextArea.SelectionBrush  = TerminalSelection;
            TextArea.SelectionForeground = TerminalForeground;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ClearScreen()
        {
            Document.Text = "";
            _segments.Clear();
            _currentStyle = new TerminalStyle();
            _leftoverStr = "";
        }

        private const int MaxCharsPerFrame = 10000; // Limit processing to prevent freeze

        public void AppendAnsiData(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            _incomingDataQueue.Enqueue(data);

            if (Interlocked.CompareExchange(ref _isRendering, 1, 0) == 0)
            {

                Dispatcher.InvokeAsync(ProcessQueue, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void ProcessQueue()
        {
            try
            {
                var sb = new StringBuilder();
                int charsProcessed = 0;

                while ((charsProcessed < MaxCharsPerFrame || _leftoverStr.Length > 0) && _incomingDataQueue.TryDequeue(out var s))
                {
                    if (_leftoverStr.Length > 0)
                    {
                        s = _leftoverStr + s;
                        _leftoverStr = "";
                    }

                    // Simple cleaning for now
                    s = s.Replace("\r\n", "\n").Replace("\r", "\n");

                    // 1. Strip Bracketed Paste Mode (\e[?2004h / \e[?2004l)
                    s = Regex.Replace(s, @"\x1b\[\?2004[hl]", "");

                    // 2. Strip Window Title OSC (\e]0;...\a)
                    s = Regex.Replace(s, @"\x1b\][0-9;]*.*?\x07", "");

                    // Check for Clear Screen sequences before stripping them
                    if (s.Contains("\x1b[2J") || s.Contains("\x1b[3J") || s.Contains("\x1b[H\x1b[2J"))
                    {
                        ClearScreen();
                        sb.Clear();
                    }

                    // 3. Strip standard CSI SGR (Color), EL/ED (Erase), and Cursor Move (H)
                    // Added H and J to the character class to catch [H, [2J, [3J
                    s = Regex.Replace(s, @"\x1b\[[0-9;]*[mKHJ]", "");

                    sb.Append(s);
                    charsProcessed += s.Length;
                }

                if (sb.Length > 0)
                {
                    Document.BeginUpdate();
                    try
                    {
                        Document.Insert(Document.TextLength, sb.ToString());
                    }
                    finally
                    {
                        Document.EndUpdate();
                    }
                    ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                try { Document.Insert(Document.TextLength, $"\r\n[Error: {ex.Message}]\r\n"); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _isRendering, 0);

                if (!_incomingDataQueue.IsEmpty)
                {
                    if (Interlocked.CompareExchange(ref _isRendering, 1, 0) == 0)
                    {
                        Dispatcher.InvokeAsync(ProcessQueue, System.Windows.Threading.DispatcherPriority.Normal);
                    }
                }
            }
        }

        private void AddTextSegment(string text, StringBuilder sb, List<ColorSegment> segments, int baseOffset, ref int relativeOffset)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Normalise line endings: \r\n → \n, lone \r → \n.
            // AvalonEdit expects \n (or \r\n) but lone \r causes the cursor to
            // overwrite the start of the current line, making output appear blank.
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");

            if (_currentStyle.IsNonDefault && text.Length > 0)
            {
                segments.Add(new ColorSegment
                {
                    StartOffset = baseOffset + relativeOffset,
                    Length = text.Length,
                    Foreground = _currentStyle.Foreground,
                    Background = _currentStyle.Background,
                    IsBold = _currentStyle.IsBold
                });
            }
            sb.Append(text);
            relativeOffset += text.Length;
        }

        private void ProcessSgr(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                _currentStyle.Reset();
                return;
            }

            var codes = content.Split(';');
            foreach (var code in codes)
            {
                if (int.TryParse(code, out int c))
                {
                    switch (c)
                    {
                        case 0: _currentStyle.Reset(); break;
                        case 1: _currentStyle.IsBold = true; break;
                        case 22: _currentStyle.IsBold = false; break;
                        case 39: _currentStyle.Foreground = null; break; // Default FG
                        case 49: _currentStyle.Background = null; break; // Default BG

                        // Foreground 30-37
                        case >= 30 and <= 37:
                            _currentStyle.Foreground = AnsiColors[c - 30 + (_currentStyle.IsBold ? 8 : 0)];
                            break;
                        // Foreground 90-97
                        case >= 90 and <= 97:
                            _currentStyle.Foreground = AnsiColors[c - 90 + 8];
                            break;

                        // Background 40-47
                        case >= 40 and <= 47:
                            _currentStyle.Background = AnsiColors[c - 40];
                            break;
                        // Background 100-107
                        case >= 100 and <= 107:
                            _currentStyle.Background = AnsiColors[c - 100 + 8];
                            break;
                    }
                }
            }
        }

        // ── Input Handling ──────────────────────────────────────────────────

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_hiddenInputCallback != null)
            {
                // Hidden mode: accumulate printable chars without echoing them
                foreach (char ch in e.Text)
                    if (ch >= ' ') // printable ASCII / Unicode (U+0020 and above)
                        _hiddenInputBuffer.Append(ch);
                e.Handled = true;
                return;
            }
            UserInput?.Invoke(e.Text);
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_hiddenInputCallback != null)
            {
                bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
                bool alt  = (e.KeyboardDevice.Modifiers & ModifierKeys.Alt)     != 0;

                // Allow Paste during hidden input
                if (ctrl && e.Key == Key.V)
                {
                    Paste();
                    e.Handled = true;
                    return;
                }

                switch (e.Key)
                {
                    case Key.Enter:
                        var enterCb = _hiddenInputCallback;
                        _hiddenInputCallback = null;
                        AppendAnsiData("\r\n");
                        enterCb(_hiddenInputBuffer.ToString());
                        _hiddenInputBuffer.Clear();
                        e.Handled = true;
                        break;
                    case Key.Back:
                        if (_hiddenInputBuffer.Length > 0)
                            _hiddenInputBuffer.Remove(_hiddenInputBuffer.Length - 1, 1);
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        var escCb = _hiddenInputCallback;
                        _hiddenInputCallback = null;
                        AppendAnsiData("\r\n");
                        escCb(string.Empty);
                        _hiddenInputBuffer.Clear();
                        e.Handled = true;
                        break;
                    default:
                        if (ctrl && e.Key == Key.C)
                        {
                            var ctrlCCb = _hiddenInputCallback;
                            _hiddenInputCallback = null;
                            AppendAnsiData("^C\r\n");
                            ctrlCCb(string.Empty);
                            _hiddenInputBuffer.Clear();
                            e.Handled = true;
                        }
                        else if (ctrl || alt)
                        {
                            e.Handled = true;
                        }
                        break;
                }
                return;
            }

            // Copy/Paste Handling
            if (e.Key == Key.C || e.Key == Key.V || e.Key == Key.Insert)
            {
                bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
                bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;

                // Copy: Ctrl+C or Ctrl+Insert (only if selection exists)
                if ((ctrl && e.Key == Key.C) || (ctrl && e.Key == Key.Insert))
                {
                    if (!TextArea.Selection.IsEmpty)
                    {
                        ApplicationCommands.Copy.Execute(null, this);
                        e.Handled = true;
                        return;
                    }
                }

                // Paste: Ctrl+V or Shift+Insert
                if ((ctrl && e.Key == Key.V) || (shift && e.Key == Key.Insert))
                {
                    Paste();
                    e.Handled = true;
                    return;
                }
            }

            // (Same key mapping logic as before)
            string? seq = KeyToSequence(e);
            if (seq != null)
            {
                UserInput?.Invoke(seq);
                e.Handled = true;
            }
        }

        private Action<string>? _hiddenInputCallback;
        private readonly StringBuilder _hiddenInputBuffer = new();

        public new void Paste()
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (_hiddenInputCallback != null)
                {
                    _hiddenInputBuffer.Append(text);
                }
                else
                {
                    UserInput?.Invoke(text);
                }
            }
        }

        public void CollectHiddenInput(string prompt, Action<string> callback)
        {
            _hiddenInputBuffer.Clear();
            _hiddenInputCallback = callback;
            AppendAnsiData(prompt);
        }

        public void CancelHiddenInput()
        {
            var cb = _hiddenInputCallback;
            _hiddenInputCallback = null;
            cb?.Invoke(string.Empty);
        }

        // ── ANSI Data Structures ─────────────────────────────────────────────

        private class TerminalStyle
        {
            public Brush? Foreground;
            public Brush? Background;
            public bool IsBold;

            public bool IsNonDefault => Foreground != null || Background != null || IsBold;

            public void Reset()
            {
                Foreground = null;
                Background = null;
                IsBold = false;
            }
        }

        private class ColorSegment : TextSegment
        {
            public Brush? Foreground;
            public Brush? Background;
            public bool IsBold;
        }

        private class AnsiColorizer : DocumentColorizingTransformer
        {
            private readonly TextSegmentCollection<ColorSegment> _segments;

            public AnsiColorizer(TextSegmentCollection<ColorSegment> segments)
            {
                _segments = segments;
            }

            public void Clear()
            {
                // Segments must be cleared from the collection
            }

            protected override void ColorizeLine(DocumentLine line)
            {
                var overlaps = _segments.FindOverlappingSegments(line);
                foreach (var seg in overlaps)
                {
                    int start = Math.Max(seg.StartOffset, line.Offset);
                    int end = Math.Min(seg.EndOffset, line.EndOffset);

                    if (end > start)
                    {
                        ChangeLinePart(start, end, element =>
                        {
                            if (seg.Foreground != null) element.TextRunProperties.SetForegroundBrush(seg.Foreground);
                            if (seg.Background != null) element.TextRunProperties.SetBackgroundBrush(seg.Background);
                            if (seg.IsBold)
                            {
                                var tf = element.TextRunProperties.Typeface;
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    tf.FontFamily, tf.Style, FontWeights.Bold, tf.Stretch));
                            }
                        });
                    }
                }
            }
        }

        // ── Standard ANSI Colors ──────────────────────────────────────────────
        private static readonly Brush[] AnsiColors =
        {
            new SolidColorBrush(Color.FromRgb(30, 30, 30)), // 0 Black (adjusted for visibility on dark bg)
            new SolidColorBrush(Color.FromRgb(197,15,31)), // 1 Red
            new SolidColorBrush(Color.FromRgb(19,161,14)), // 2 Green
            new SolidColorBrush(Color.FromRgb(193,156,0)), // 3 Yellow
            // User requested blue links to be "removed" (changed to default text color)
            // Was: new SolidColorBrush(Color.FromRgb(59, 142, 234))
            new SolidColorBrush(Color.FromRgb(204, 204, 204)), // 4 Blue -> Default White/Gray
            new SolidColorBrush(Color.FromRgb(136,23,152)),// 5 Magenta
            new SolidColorBrush(Color.FromRgb(58,150,221)),// 6 Cyan
            new SolidColorBrush(Color.FromRgb(204,204,204)),// 7 White
            new SolidColorBrush(Color.FromRgb(118,118,118)),// 8 Bright Black
            new SolidColorBrush(Color.FromRgb(231,72,86)), // 9 Bright Red
            new SolidColorBrush(Color.FromRgb(22,198,12)), // 10 Bright Green
            new SolidColorBrush(Color.FromRgb(249,241,165)),// 11 Bright Yellow
            // Was: new SolidColorBrush(Color.FromRgb(59,120,255))
            new SolidColorBrush(Color.FromRgb(204, 204, 204)), // 12 Bright Blue -> Default White/Gray
            new SolidColorBrush(Color.FromRgb(180,0,158)), // 13 Bright Magenta
            new SolidColorBrush(Color.FromRgb(97,214,214)),// 14 Bright Cyan
            new SolidColorBrush(Color.FromRgb(242,242,242)),// 15 Bright White
        };


        private static string? KeyToSequence(KeyEventArgs e)
        {
             bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
            switch (e.Key)
            {
                case Key.Enter:     return "\r";
                case Key.Space:     return " ";
                case Key.Back:      return "\x7f";
                case Key.Tab:       return "\t";
                case Key.Escape:    return "\x1b";
                case Key.Up:        return "\x1b[A";
                case Key.Down:      return "\x1b[B";
                case Key.Right:     return "\x1b[C";
                case Key.Left:      return "\x1b[D";
                case Key.Home:      return "\x1b[H";
                case Key.End:       return "\x1b[F";
                case Key.Delete:    return "\x1b[3~";
                case Key.Insert:    return "\x1b[2~";
                case Key.PageUp:    return "\x1b[5~";
                case Key.PageDown:  return "\x1b[6~";
                case Key.F1:        return "\x1bOP";
                case Key.F2:        return "\x1bOQ";
                case Key.F3:        return "\x1bOR";
                case Key.F4:        return "\x1bOS";
                case Key.F5:        return "\x1b[15~";
                case Key.F6:        return "\x1b[17~";
                case Key.F7:        return "\x1b[18~";
                case Key.F8:        return "\x1b[19~";
                case Key.F9:        return "\x1b[20~";
                case Key.F10:       return "\x1b[21~";
                case Key.F11:       return "\x1b[23~";
                case Key.F12:       return "\x1b[24~";
                default:
                    if (ctrl && e.Key >= Key.A && e.Key <= Key.Z)
                        return ((char)(e.Key - Key.A + 1)).ToString();
                    return null;
            }
        }
    }
}

