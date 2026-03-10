using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace PowerTerminal.Controls
{
    /// <summary>
    /// A simple VT100/ANSI terminal emulator control rendered in a WPF RichTextBox.
    /// Supports color attributes, basic cursor movement, and screen scrollback.
    /// </summary>
    public class TerminalControl : RichTextBox
    {
        // ── Dependency Properties ─────────────────────────────────────────────
        public static readonly DependencyProperty TerminalFontFamilyProperty =
            DependencyProperty.Register(nameof(TerminalFontFamily), typeof(FontFamily),
                typeof(TerminalControl), new PropertyMetadata(new FontFamily("Cascadia Code, Consolas, Courier New"),
                    (d, e) => ((TerminalControl)d).UpdateFont()));

        public static readonly DependencyProperty TerminalFontSizeProperty =
            DependencyProperty.Register(nameof(TerminalFontSize), typeof(double),
                typeof(TerminalControl), new PropertyMetadata(13.0,
                    (d, e) => ((TerminalControl)d).UpdateFont()));

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

        // ── VT100 Color palette ───────────────────────────────────────────────
        private static readonly Color[] AnsiColors =
        {
            Colors.Black,           // 0 - Black
            Color.FromRgb(197,15,31), // 1 - Red
            Color.FromRgb(19,161,14), // 2 - Green
            Color.FromRgb(193,156,0), // 3 - Yellow
            Color.FromRgb(0,55,218),  // 4 - Blue
            Color.FromRgb(136,23,152),// 5 - Magenta
            Color.FromRgb(58,150,221),// 6 - Cyan
            Color.FromRgb(204,204,204),// 7 - White
            Color.FromRgb(118,118,118),// 8 - Bright Black (Gray)
            Color.FromRgb(231,72,86), // 9 - Bright Red
            Color.FromRgb(22,198,12), // 10 - Bright Green
            Color.FromRgb(249,241,165),// 11 - Bright Yellow
            Color.FromRgb(59,120,255),// 12 - Bright Blue
            Color.FromRgb(180,0,158), // 13 - Bright Magenta
            Color.FromRgb(97,214,214),// 14 - Bright Cyan
            Color.FromRgb(242,242,242),// 15 - Bright White
        };

        // Current text attributes
        private Brush _fg = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        private Brush _bg = Brushes.Transparent;
        private bool _bold;
        private bool _underline;

        private Paragraph _currentParagraph = new();
        private readonly StringBuilder _escBuffer = new();
        private bool _inEscape;
        private bool _inOsc;
        private readonly StringBuilder _oscBuffer = new();

        // ── Hidden-input mode (for inline password collection) ─────────────────
        private Action<string>? _hiddenInputCallback;
        private readonly StringBuilder _hiddenInputBuffer = new();

        // ── Blinking cursor ───────────────────────────────────────────────────
        private readonly Run _cursorRun;
        private readonly System.Windows.Threading.DispatcherTimer _cursorTimer;
        private bool _cursorVisible = true;
        private bool _pendingCR;
        private const int CursorBlinkIntervalMs = 530;

        private const int MaxParagraphs = 5000;

        public event Action<string>? UserInput;

        public TerminalControl()
        {
            IsReadOnly    = true;
            IsDocumentEnabled = true;
            Background    = new SolidColorBrush(Color.FromRgb(12, 12, 12));
            Foreground    = new SolidColorBrush(Color.FromRgb(204, 204, 204));
            FontFamily    = new FontFamily("Cascadia Code, Consolas, Courier New");
            FontSize      = 16;
            BorderThickness = new Thickness(0);
            Padding       = new Thickness(4);
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            Document.LineHeight = 16;
            Document.Blocks.Clear();
            _currentParagraph.Margin = new Thickness(0);

            // Blinking block cursor — always kept as the last inline of _currentParagraph
            _cursorRun = new Run("|")
            {
                Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                FontSize   = 16
            };
            _currentParagraph.Inlines.Add(_cursorRun);
            Document.Blocks.Add(_currentParagraph);

            // Toggle cursor visibility every CursorBlinkIntervalMs
            _cursorTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(CursorBlinkIntervalMs)
            };
            _cursorTimer.Tick += OnCursorTick;
            _cursorTimer.Start();

            Unloaded += OnControlUnloaded;
            Loaded   += OnControlLoaded;

            PreviewKeyDown   += OnPreviewKeyDown;
            PreviewTextInput += OnPreviewTextInput;

            Focusable = true;
        }

        private void OnCursorTick(object? sender, EventArgs e)
        {
            _cursorVisible = !_cursorVisible;
            _cursorRun.Text = _cursorVisible ? "▋" : "";
        }

        private void OnControlUnloaded(object sender, RoutedEventArgs e) => _cursorTimer.Stop();
        private void OnControlLoaded(object sender, RoutedEventArgs e)   => _cursorTimer.Start();

        private void UpdateFont()
        {
            FontFamily = TerminalFontFamily;
            FontSize   = TerminalFontSize;
        }

        // ── Input handling ────────────────────────────────────────────────────

        /// <summary>
        /// Prints <paramref name="prompt"/> in the terminal and switches to hidden-input mode.
        /// Every keystroke is accumulated without display until Enter is pressed, at which point
        /// <paramref name="callback"/> is invoked with the collected text and normal mode resumes.
        /// Escape and Ctrl+C cancel the collection and invoke <paramref name="callback"/> with
        /// an empty string.  Must be called on the UI thread.
        /// </summary>
        public void CollectHiddenInput(string prompt, Action<string> callback)
        {
            _hiddenInputBuffer.Clear();
            _hiddenInputCallback = callback;
            AppendAnsiData(prompt);
        }

        /// <summary>
        /// Cancels any in-progress hidden-input collection, invoking the pending callback
        /// with an empty string so that the waiting SSH background thread is unblocked.
        /// Safe to call from the UI thread at any time.
        /// </summary>
        public void CancelHiddenInput()
        {
            var cb = _hiddenInputCallback;
            _hiddenInputCallback = null;
            _hiddenInputBuffer.Clear();
            cb?.Invoke(string.Empty);
        }

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
                            // Suppress other modifier combinations (Ctrl+Z, Alt+F4, etc.)
                            // so they don't accidentally trigger RichTextBox commands while
                            // the user is typing a password.
                            e.Handled = true;
                        }
                        // Plain printable key: do NOT set e.Handled here.
                        // WPF will generate a WM_CHAR message which fires PreviewTextInput,
                        // and OnPreviewTextInput will buffer the character.  Setting
                        // e.Handled = true here would suppress WM_CHAR and break buffering.
                        break;
                }
                return;
            }

            string? seq = KeyToSequence(e);
            if (seq != null)
            {
                UserInput?.Invoke(seq);
                e.Handled = true;
            }
        }

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

        // ── VT100 parser ──────────────────────────────────────────────────────

        public void AppendAnsiData(string data)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (char ch in data)
                    ParseChar(ch);
                ScrollToEnd();
                TrimHistory();
            });
        }

        public void ClearScreen()
        {
            Dispatcher.Invoke(() =>
            {
                _pendingCR = false;
                Document.Blocks.Clear();
                _currentParagraph = new Paragraph { Margin = new Thickness(0) };
                _currentParagraph.Inlines.Add(_cursorRun);
                Document.Blocks.Add(_currentParagraph);
            });
        }

        private void ParseChar(char ch)
        {
            if (_inOsc)
            {
                if (ch == '\x07' || ch == '\x9c')
                {
                    _inOsc = false;
                    _oscBuffer.Clear();
                }
                else
                {
                    _oscBuffer.Append(ch);
                }
                return;
            }

            if (_inEscape)
            {
                _escBuffer.Append(ch);
                if (IsEscapeTerminator(ch))
                {
                    ProcessEscape(_escBuffer.ToString());
                    _escBuffer.Clear();
                    _inEscape = false;
                }
                return;
            }

            // Lone CR (not followed by LF): move cursor to column 0 — overwrite the current line.
            // CR+LF pair: treat as a normal newline; the CR just resets the pending flag.
            if (_pendingCR && ch != '\n')
            {
                _pendingCR = false;
                ClearCurrentLine();
            }

            switch (ch)
            {
                case '\x1b':
                    _inEscape = true;
                    _escBuffer.Clear();
                    break;
                case '\x9b':                  // CSI shortcut
                    _inEscape = true;
                    _escBuffer.Append('[');
                    break;
                case '\x9d':                  // OSC shortcut
                    _inOsc = true;
                    _oscBuffer.Clear();
                    break;
                case '\r':
                    _pendingCR = true;
                    break;
                case '\n':
                    _pendingCR = false;
                    // Move cursor to the new paragraph
                    _currentParagraph.Inlines.Remove(_cursorRun);
                    _currentParagraph = new Paragraph { Margin = new Thickness(0) };
                    _currentParagraph.Inlines.Add(_cursorRun);
                    Document.Blocks.Add(_currentParagraph);
                    break;
                case '\x07':                  // Bell – ignore
                    break;
                case '\b':                    // Backspace
                    RemoveLastChar();
                    break;
                default:
                    if (ch >= ' ')
                        AppendChar(ch);
                    break;
            }
        }

        /// <summary>
        /// Clears all content from the current line (keeping the cursor run), simulating
        /// the VT100 "erase to end of line" / carriage-return-overwrite behaviour.
        /// Must be called on the UI thread.
        /// </summary>
        private void ClearCurrentLine()
        {
            _currentParagraph.Inlines.Remove(_cursorRun);
            _currentParagraph.Inlines.Clear();
            _currentParagraph.Inlines.Add(_cursorRun);
        }

        private static bool IsEscapeTerminator(char ch)
        {
            // Multi-char sequence terminator rules:
            // After ESC [ ... : letter terminates
            // After ESC ] ... : ST or BEL terminates
            return (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
                   ch == '@' || ch == '`' || ch == '~' ||
                   (ch == '\\' && !string.IsNullOrEmpty(string.Empty)); // ESC-only
        }

        private void ProcessEscape(string seq)
        {
            if (seq.StartsWith("["))
            {
                ProcessCsi(seq.Substring(1));
            }
            else if (seq.StartsWith("]"))
            {
                // OSC - set title, ignore
            }
            else if (seq == "c")
            {
                ResetAttributes();
                ClearScreen();
            }
        }

        private void ProcessCsi(string seq)
        {
            if (seq.Length == 0) return;
            char cmd = seq[^1];
            string paramStr = seq.Length > 1 ? seq.Substring(0, seq.Length - 1) : string.Empty;

            switch (cmd)
            {
                case 'm': // SGR - Set Graphic Rendition
                    ProcessSgr(paramStr);
                    break;
                case 'J': // Erase in display
                    if (paramStr == "2" || paramStr == "") ClearScreen();
                    break;
                case 'H': // Cursor Position
                case 'f':
                    // For now, start a new line when repositioning to top
                    if (paramStr == "" || paramStr == "1;1" || paramStr == "0;0")
                        ClearScreen();
                    break;
                case 'K': // Erase in line
                    // 0 (or omitted) = erase cursor to end; 1 = erase start to cursor; 2 = erase whole line.
                    // Our cursor is always at end of line in the append model, so 0 and 2 both clear the line.
                    {
                        int kParam = 0;
                        if (paramStr != "" && int.TryParse(paramStr, out int kp)) kParam = kp;
                        if (kParam == 0 || kParam == 2)
                            ClearCurrentLine();
                    }
                    break;
                // Cursor movement – we rely on the shell to re-draw
                case 'A': case 'B': case 'C': case 'D':
                case 'E': case 'F': case 'G': case 'd':
                case 'S': case 'T': case 'L': case 'M':
                case 'P': case 'X': case '@':
                    break;
                // Mode settings
                case 'h': case 'l':
                    break;
            }
        }

        private void ProcessSgr(string paramStr)
        {
            var parts = paramStr.Split(';');
            int i = 0;
            while (i < parts.Length)
            {
                if (!int.TryParse(parts[i], out int code))
                {
                    i++;
                    continue;
                }

                switch (code)
                {
                    case 0:  ResetAttributes(); break;
                    case 1:  _bold = true;      break;
                    case 4:  _underline = true; break;
                    case 22: _bold = false;     break;
                    case 24: _underline = false; break;

                    // Foreground colors 30-37
                    case >= 30 and <= 37:
                        _fg = new SolidColorBrush(AnsiColors[code - 30 + (_bold ? 8 : 0)]);
                        break;
                    case 39: // default fg
                        _fg = new SolidColorBrush(Color.FromRgb(204, 204, 204));
                        break;
                    // Background colors 40-47
                    case >= 40 and <= 47:
                        _bg = new SolidColorBrush(AnsiColors[code - 40]);
                        break;
                    case 49: // default bg
                        _bg = Brushes.Transparent;
                        break;
                    // Bright foreground 90-97
                    case >= 90 and <= 97:
                        _fg = new SolidColorBrush(AnsiColors[code - 90 + 8]);
                        break;
                    // Bright background 100-107
                    case >= 100 and <= 107:
                        _bg = new SolidColorBrush(AnsiColors[code - 100 + 8]);
                        break;
                    // 256-color and RGB foreground
                    case 38:
                        if (i + 2 < parts.Length && parts[i + 1] == "5")
                        {
                            if (int.TryParse(parts[i + 2], out int idx256))
                                _fg = new SolidColorBrush(Get256Color(idx256));
                            i += 2;
                        }
                        else if (i + 4 < parts.Length && parts[i + 1] == "2")
                        {
                            if (byte.TryParse(parts[i + 2], out byte r) &&
                                byte.TryParse(parts[i + 3], out byte g) &&
                                byte.TryParse(parts[i + 4], out byte b))
                                _fg = new SolidColorBrush(Color.FromRgb(r, g, b));
                            i += 4;
                        }
                        break;
                    // 256-color and RGB background
                    case 48:
                        if (i + 2 < parts.Length && parts[i + 1] == "5")
                        {
                            if (int.TryParse(parts[i + 2], out int idx256))
                                _bg = new SolidColorBrush(Get256Color(idx256));
                            i += 2;
                        }
                        else if (i + 4 < parts.Length && parts[i + 1] == "2")
                        {
                            if (byte.TryParse(parts[i + 2], out byte r) &&
                                byte.TryParse(parts[i + 3], out byte g) &&
                                byte.TryParse(parts[i + 4], out byte b))
                                _bg = new SolidColorBrush(Color.FromRgb(r, g, b));
                            i += 4;
                        }
                        break;
                }
                i++;
            }
        }

        private void ResetAttributes()
        {
            _fg        = new SolidColorBrush(Color.FromRgb(204, 204, 204));
            _bg        = Brushes.Transparent;
            _bold      = false;
            _underline = false;
        }

        private void AppendChar(char ch)
        {
            // Keep cursor as the last inline — insert new content before it
            _currentParagraph.Inlines.Remove(_cursorRun);
            var run = new Run(ch.ToString())
            {
                Foreground  = _fg,
                Background  = _bg,
                FontWeight  = _bold ? FontWeights.Bold : FontWeights.Normal,
                TextDecorations = _underline ? TextDecorations.Underline : null,
                FontFamily  = FontFamily,
                FontSize    = FontSize
            };
            _currentParagraph.Inlines.Add(run);
            _currentParagraph.Inlines.Add(_cursorRun);
        }

        private void RemoveLastChar()
        {
            // Temporarily remove cursor to expose the last real character
            bool hadCursor = _currentParagraph.Inlines.LastInline == _cursorRun;
            if (hadCursor) _currentParagraph.Inlines.Remove(_cursorRun);

            if (_currentParagraph.Inlines.LastInline is Run r && r.Text.Length > 0)
                r.Text = r.Text.Substring(0, r.Text.Length - 1);

            if (hadCursor) _currentParagraph.Inlines.Add(_cursorRun);
        }

        private void TrimHistory()
        {
            while (Document.Blocks.Count > MaxParagraphs)
            {
                var first = Document.Blocks.FirstBlock;
                // Always keep at least one paragraph (the current one that holds the cursor)
                if (Document.Blocks.Count <= 1) break;
                Document.Blocks.Remove(first);
            }
        }

        private static Color Get256Color(int idx)
        {
            if (idx < 16)
                return AnsiColors[idx];
            if (idx < 232)
            {
                idx -= 16;
                int r = (idx / 36) * 51;
                int g = ((idx % 36) / 6) * 51;
                int b = (idx % 6) * 51;
                return Color.FromRgb((byte)r, (byte)g, (byte)b);
            }
            // Grayscale
            int gray = 8 + (idx - 232) * 10;
            gray = Math.Min(gray, 238);
            return Color.FromRgb((byte)gray, (byte)gray, (byte)gray);
        }
    }
}
