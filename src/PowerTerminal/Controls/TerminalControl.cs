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
            WordWrap = true; // Enable wrapping
            FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New");
            FontSize = 13;
            Background = TerminalBackground;
            Foreground = TerminalForeground;

            // Critical for performance with large outputs
            Options.EnableTextDragDrop = false;
            Options.EnableRectangularSelection = false;
            Options.EnableHyperlinks = false;
            Options.EnableEmailHyperlinks = false;
            Options.AllowScrollBelowDocument = false;

            // Ensure no link generators remain (force removal of built-in ones if options fail)
            // Using a loop to find and remove types responsible for links without hard dependency on specific class names if possible
            // but usually it is LinkElementGenerator.
            // We defer this check to Loaded or just after applying template to be safe,
            // but constructor is usually fine for default ones.

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
                var newSegments = new List<ColorSegment>();

                int baseOffset = Document.TextLength;
                int relativeOffset = 0;
                int charsProcessed = 0;

                while ((charsProcessed < MaxCharsPerFrame || _leftoverStr.Length > 0) && _incomingDataQueue.TryDequeue(out var s))
                {
                    if (_leftoverStr.Length > 0)
                    {
                        s = _leftoverStr + s;
                        _leftoverStr = "";
                    }

                    // 1. Basic newline cleaning
                    s = s.Replace("\r\n", "\n").Replace("\r", "\n");

                    // 2. Strip Bracketed Paste Mode (\e[?2004h / \e[?2004l)
                    s = Regex.Replace(s, @"\x1b\[\?2004[hl]", "");

                    // 3. Strip Window Title OSC (\e]0;...\a)
                    s = Regex.Replace(s, @"\x1b\][0-9;]*.*?\x07", "");

                    charsProcessed += s.Length;

                    // 4. Regex split to separate ANSI codes from text
                    // Splits on CSI (ESC[ ... X) and keeps delimiters
                    string[] parts = Regex.Split(s, "(\\x1b\\[[0-9;?]*[A-Za-z])");

                    for (int i = 0; i < parts.Length; i++)
                    {
                        string part = parts[i];
                        if (string.IsNullOrEmpty(part)) continue;

                        if (part.StartsWith("\x1b["))
                        {
                            // CSI (Control Sequence Introducer)
                            char finalChar = part[part.Length - 1];
                            string csiParam = part.Length >= 3 ? part.Substring(2, part.Length - 3) : string.Empty;

                            switch (finalChar)
                            {
                                case 'm':
                                    // SGR — color/style
                                    ProcessSgr(csiParam);
                                    break;

                                case 'J':
                                    // ED — Erase Display.
                                    if (csiParam == "2" || csiParam == "3" || csiParam == "" || csiParam == "0")
                                    {
                                        // Flush buffer before clearing
                                        if (sb.Length > 0)
                                        {
                                            Document.BeginUpdate();
                                            try
                                            {
                                                Document.Insert(Document.TextLength, sb.ToString());
                                                foreach (var seg in newSegments)
                                                {
                                                    if (seg.StartOffset + seg.Length <= Document.TextLength)
                                                        _segments.Add(seg);
                                                }
                                            }
                                            finally { Document.EndUpdate(); }
                                            sb.Clear();
                                            newSegments.Clear();
                                        }
                                        ClearScreen();
                                        baseOffset = 0;
                                        relativeOffset = 0;
                                    }
                                    break;

                                case 'H':
                                case 'f':
                                    // CUP — Cursor Position. Ignored for scrolling terminal.
                                    break;

                                case 'K':
                                    // EL — Erase in Line. Ignored.
                                    break;

                                default:
                                    // Other CSI — Ignored
                                    break;
                            }
                        }
                        else
                        {
                            // Check for partial ANSI sequence at the end
                            if (i == parts.Length - 1 && part.Contains("\x1b"))
                            {
                                int escIndex = part.LastIndexOf('\x1b');
                                if (escIndex >= 0)
                                {
                                    if (escIndex > 0)
                                    {
                                        string txt = part.Substring(0, escIndex);
                                        AddTextSegment(txt, sb, newSegments, baseOffset, ref relativeOffset);
                                    }
                                    _leftoverStr = part.Substring(escIndex);
                                    continue;
                                }
                            }

                            // Normal text
                            AddTextSegment(part, sb, newSegments, baseOffset, ref relativeOffset);
                        }
                    }
                }

                if (sb.Length > 0)
                {
                    Document.BeginUpdate();
                    try
                    {
                        Document.Insert(Document.TextLength, sb.ToString());
                        foreach (var seg in newSegments)
                        {
                            if (seg.StartOffset + seg.Length <= Document.TextLength)
                                _segments.Add(seg);
                        }
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

        private void ProcessSgr(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                _currentStyle.Reset();
                return;
            }

            var parts = content.Split(';');
            var codes = new List<int>();
            foreach (var p in parts)
            {
                if (int.TryParse(p, out int val)) codes.Add(val);
                else codes.Add(0); // Handle empty/invalid as 0 (Reset) or ignore? Usually 0.
            }

            if (codes.Count == 0)
            {
                _currentStyle.Reset();
                return;
            }

            for (int i = 0; i < codes.Count; i++)
            {
                int code = codes[i];

                switch (code)
                {
                    case 0: _currentStyle.Reset(); break;
                    case 1: _currentStyle.IsBold = true; break;
                    case 22: _currentStyle.IsBold = false; break;
                    case 39: _currentStyle.Foreground = null; break; // Default FG
                    case 49: _currentStyle.Background = null; break; // Default BG

                    // Foreground Standard (30-37)
                    case >= 30 and <= 37:
                        _currentStyle.Foreground = GetAnsiColor(code - 30, _currentStyle.IsBold);
                        break;
                    // Foreground Bright (90-97)
                    case >= 90 and <= 97:
                        _currentStyle.Foreground = GetAnsiColor(code - 90 + 8, false);
                        break;

                    // Background Standard (40-47)
                    case >= 40 and <= 47:
                        _currentStyle.Background = GetAnsiColor(code - 40, false); // BG rarely bold
                        break;
                    // Background Bright (100-107)
                    case >= 100 and <= 107:
                        _currentStyle.Background = GetAnsiColor(code - 100 + 8, false);
                        break;

                    // Extended Colors (38/48)
                    case 38: // FG
                    case 48: // BG
                        if (i + 1 < codes.Count)
                        {
                            int type = codes[i + 1];
                            if (type == 5 && i + 2 < codes.Count)
                            {
                                // 256-color: 38;5;n
                                int colorIndex = codes[i + 2];
                                var brush = GetXtermColor(colorIndex);
                                if (code == 38) _currentStyle.Foreground = brush;
                                else            _currentStyle.Background = brush;
                                i += 2; // Skip processed
                            }
                            else if (type == 2 && i + 4 < codes.Count)
                            {
                                // TrueColor: 38;2;r;g;b
                                byte r = (byte)codes[i + 2];
                                byte g = (byte)codes[i + 3];
                                byte b = (byte)codes[i + 4];
                                var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                                brush.Freeze();
                                if (code == 38) _currentStyle.Foreground = brush;
                                else            _currentStyle.Background = brush;
                                i += 4; // Skip processed
                            }
                        }
                        break;
                }
            }
        }

        private Brush GetAnsiColor(int index, bool isBold)
        {
            // Map 0-7 to 8-15 if bold is active (common terminal behavior for FG)
            if (isBold && index < 8) index += 8;
            if (index >= 0 && index < AnsiColors.Length) return AnsiColors[index];
            return AnsiColors[7]; // Fallback
        }

        private static readonly ConcurrentDictionary<int, Brush> XtermCache = new();

        private static Brush GetXtermColor(int index)
        {
            if (index < 0 || index > 255) return AnsiColors[7]; // Fallback
            if (index < 16) return AnsiColors[index];           // 0-15 Standard

            return XtermCache.GetOrAdd(index, idx =>
            {
                Color c;
                if (idx >= 16 && idx <= 231)
                {
                    // 6x6x6 Color Cube
                    // (val - 16) = (r * 36) + (g * 6) + b
                    int val = idx - 16;
                    int b = val % 6; val /= 6;
                    int g = val % 6; val /= 6;
                    int r = val;

                    // Mapping 0..5 to 0..255
                    // 0->0, 1->95, 2->135, 3->175, 4->215, 5->255
                    byte ByteMap(int v) => (byte)(v == 0 ? 0 : (v * 40 + 55));
                    c = Color.FromRgb(ByteMap(r), ByteMap(g), ByteMap(b));
                }
                else
                {
                    // 232-255 Grayscale Ramp
                    // (val - 232) * 10 + 8
                    int gray = (idx - 232) * 10 + 8;
                    c = Color.FromRgb((byte)gray, (byte)gray, (byte)gray);
                }
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            });
        }

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

        // ── Standard ANSI Colors (Ubuntu / Tango Scheme) ──────────────────────
        private static readonly Brush[] AnsiColors =
        {
            // 0-7: Normal
            new SolidColorBrush(Color.FromRgb(46, 52, 54)),   // 0 Black
            new SolidColorBrush(Color.FromRgb(204, 0, 0)),    // 1 Red
            new SolidColorBrush(Color.FromRgb(78, 154, 6)),   // 2 Green
            new SolidColorBrush(Color.FromRgb(196, 160, 0)),  // 3 Yellow
            new SolidColorBrush(Color.FromRgb(52, 101, 164)), // 4 Blue (Restored)
            new SolidColorBrush(Color.FromRgb(117, 80, 123)), // 5 Magenta
            new SolidColorBrush(Color.FromRgb(6, 152, 154)),  // 6 Cyan
            new SolidColorBrush(Color.FromRgb(211, 215, 207)),// 7 White

            // 8-15: Bright (Bold)
            new SolidColorBrush(Color.FromRgb(85, 87, 83)),   // 8 Bright Black
            new SolidColorBrush(Color.FromRgb(239, 41, 41)),  // 9 Bright Red
            new SolidColorBrush(Color.FromRgb(138, 226, 52)), // 10 Bright Green
            new SolidColorBrush(Color.FromRgb(252, 233, 79)), // 11 Bright Yellow
            new SolidColorBrush(Color.FromRgb(114, 159, 207)),// 12 Bright Blue (Restored)
            new SolidColorBrush(Color.FromRgb(173, 127, 168)),// 13 Bright Magenta
            new SolidColorBrush(Color.FromRgb(52, 226, 226)), // 14 Bright Cyan
            new SolidColorBrush(Color.FromRgb(238, 238, 236)),// 15 Bright White
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

