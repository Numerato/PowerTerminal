#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Text.RegularExpressions;

namespace PowerTerminal.Controls
{
    /// <summary>
    /// A VT100/xterm terminal emulator control backed by a 2D character buffer.
    /// Renders into an AvalonEdit TextEditor. Supports full cursor positioning,
    /// scrolling regions, alternate screen buffer, and 256/TrueColor.
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

        // ── Terminal buffer ──────────────────────────────────────────────────
        private TerminalBuffer _buffer;
        private const int DefaultRows = 24;
        private const int DefaultCols = 80;

        // ── Rendering ────────────────────────────────────────────────────────
        private readonly TextSegmentCollection<ColorSegment> _segments;
        private readonly AnsiColorizer _colorizer;

        public event Action<string>? UserInput;
        /// <summary>Raised when an OSC sequence sets the window title (OSC 0/1/2).</summary>
        public event Action<string>? TitleChanged;
        /// <summary>Raised when the terminal buffer dimensions change due to control resize.</summary>
        public event Action<uint, uint>? TerminalResized;
        /// <summary>Raised when the terminal wants a visual bell (e.g. flash the tab header).</summary>
        public event Action? BellRung;

        // Queue for background appending to avoid UI freeze
        private readonly ConcurrentQueue<string> _incomingDataQueue = new();
        private string _leftoverStr = "";
        private int _isRendering;

        // Resize tracking
        private double _charWidth;
        private double _charHeight;
        private bool _charSizeMeasured;

        /// <summary>Height of one terminal line in WPF device-independent units.</summary>
        public double CharHeight
        {
            get
            {
                if (!_charSizeMeasured) MeasureCharSize();
                return _charHeight;
            }
        }

        private static readonly SolidColorBrush TerminalForeground = new(Color.FromRgb(204, 204, 204));
        private static readonly SolidColorBrush TerminalBackground = new(Color.FromRgb(12, 12, 12));
        private static readonly SolidColorBrush TerminalCaret      = new(Color.FromRgb(204, 204, 204));
        private static readonly SolidColorBrush TerminalCaretHidden = new(Color.FromArgb(0, 0, 0, 0));
        private static readonly SolidColorBrush TerminalSelection  = new(Color.FromArgb(120, 92, 40, 0));

        static TerminalControl()
        {
            TerminalForeground.Freeze();
            TerminalBackground.Freeze();
            TerminalCaret.Freeze();
            TerminalCaretHidden.Freeze();
            TerminalSelection.Freeze();
            foreach (var b in AnsiColors) b.Freeze();
        }

        public TerminalControl()
        {
            _buffer = new TerminalBuffer(DefaultRows, DefaultCols);

            IsReadOnly = true;
            ShowLineNumbers = false;
            WordWrap = false;
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

            _segments = new TextSegmentCollection<ColorSegment>(this.Document);
            _colorizer = new AnsiColorizer(_segments);
            TextArea.TextView.LineTransformers.Add(_colorizer);

            // Apply colors to the TextArea immediately (template may already be applied)
            ApplyTerminalColors();

            PreviewKeyDown += OnPreviewKeyDown;
            PreviewTextInput += OnPreviewTextInput;

            Loaded += (_, _) => ApplyTerminalColors();

            // Dynamic resize: recalculate terminal dimensions when control size changes
            SizeChanged += OnSizeChanged;

            // Focus reporting for ?1004
            GotFocus += (_, _) => { if (_buffer.FocusReporting) UserInput?.Invoke("\x1b[I"); };
            LostFocus += (_, _) => { if (_buffer.FocusReporting) UserInput?.Invoke("\x1b[O"); };

            // Mouse events for terminal mouse reporting
            PreviewMouseDown += OnPreviewMouseDown;
            PreviewMouseUp += OnPreviewMouseUp;
            PreviewMouseMove += OnPreviewMouseMove;
            PreviewMouseWheel += OnPreviewMouseWheel;

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
            ApplyTerminalColors();
        }

        private void ApplyTerminalColors()
        {
            TextArea.Foreground        = TerminalForeground;
            TextArea.Background        = TerminalBackground;
            TextArea.TextView.NonPrintableCharacterBrush = TerminalForeground;
            TextArea.Caret.CaretBrush = TerminalCaret;
            TextArea.SelectionBrush  = TerminalSelection;
            TextArea.SelectionForeground = TerminalForeground;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ClearScreen()
        {
            _buffer.FullReset();
            RenderBuffer();
            _leftoverStr = "";
        }

        public void AppendAnsiData(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            _incomingDataQueue.Enqueue(data);

            if (Interlocked.CompareExchange(ref _isRendering, 1, 0) == 0)
            {
                // Use Background priority so the UI stays responsive during large output
                // bursts (e.g. "apt list"). Higher-priority input/render events can
                // interleave between processing batches.
                Dispatcher.InvokeAsync(ProcessQueue, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // ── Escape Sequence Parser (character-by-character) ──────────────────

        // Parsing is lightweight (buffer ops, no document mutation until render).
        // Rendering cost is bounded by terminal dimensions, not input size.
        private const int MaxCharsPerFrame = 50000;

        private void ProcessQueue()
        {
            try
            {
                int charsProcessed = 0;

                while ((charsProcessed < MaxCharsPerFrame || _leftoverStr.Length > 0)
                       && _incomingDataQueue.TryDequeue(out var s))
                {
                    if (_leftoverStr.Length > 0)
                    {
                        s = _leftoverStr + s;
                        _leftoverStr = "";
                    }

                    charsProcessed += s.Length;
                    ParseData(s);
                }

                if (_buffer.IsDirty)
                {
                    RenderBuffer();
                    _buffer.IsDirty = false;
                }
            }
            catch (Exception ex)
            {
                try { Document.Insert(Document.TextLength, $"\n[Error: {ex.Message}]\n"); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _isRendering, 0);

                if (!_incomingDataQueue.IsEmpty)
                {
                    if (Interlocked.CompareExchange(ref _isRendering, 1, 0) == 0)
                    {
                        Dispatcher.InvokeAsync(ProcessQueue, System.Windows.Threading.DispatcherPriority.Background);
                    }
                }
            }
        }

        /// <summary>
        /// Parse incoming data character-by-character, dispatching to the buffer
        /// or escape sequence handlers as appropriate.
        /// </summary>
        private void ParseData(string data)
        {
            int i = 0;
            int len = data.Length;

            while (i < len)
            {
                char c = data[i];

                if (c == '\x1b') // ESC
                {
                    if (i + 1 >= len)
                    {
                        // Incomplete escape — save remainder for next chunk.
                        // Allocation is acceptable: occurs only at network chunk boundaries.
                        _leftoverStr = data.Substring(i);
                        return;
                    }

                    char next = data[i + 1];

                    if (next == '[')
                    {
                        // CSI sequence: ESC [ ...
                        i = ParseCsi(data, i + 2);
                    }
                    else if (next == ']')
                    {
                        // OSC sequence: ESC ] ... BEL/ST
                        i = ParseOsc(data, i + 2);
                    }
                    else if (next == '(' || next == ')')
                    {
                        // Character set designation — skip (ESC ( X or ESC ) X)
                        i = i + 2 < len ? i + 3 : len;
                    }
                    else if (next == '#')
                    {
                        // ESC # sequences (e.g. DECALN)
                        if (i + 2 < len)
                        {
                            ProcessEscHash(data[i + 2]);
                            i += 3;
                        }
                        else
                        {
                            _leftoverStr = data.Substring(i);
                            return;
                        }
                    }
                    else
                    {
                        // Single-character ESC sequences
                        ProcessEscChar(next);
                        i += 2;
                    }
                }
                else if (c < ' ' || c == '\x7f')
                {
                    // Control character
                    ProcessControlChar(c);
                    i++;
                }
                else
                {
                    // Regular printable character — batch for efficiency
                    int start = i;
                    while (i < len && data[i] >= ' ' && data[i] != '\x1b' && data[i] != '\x7f')
                        i++;

                    // Write batch of characters
                    for (int j = start; j < i; j++)
                        _buffer.WriteChar(data[j]);
                }
            }
        }

        /// <summary>Parse a CSI sequence starting after ESC[. Returns next index.</summary>
        private int ParseCsi(string data, int start)
        {
            int i = start;
            int len = data.Length;

            // Collect prefix character (?, >, !)
            string prefix = "";
            if (i < len && (data[i] == '?' || data[i] == '>' || data[i] == '!'))
            {
                prefix = data[i].ToString();
                i++;
            }

            // Collect parameter string (digits and semicolons)
            int paramStart = i;
            while (i < len && (char.IsDigit(data[i]) || data[i] == ';'))
                i++;
            string paramStr = data.Substring(paramStart, i - paramStart);

            // Collect intermediate bytes (0x20-0x2F: space through /)
            string intermediate = "";
            while (i < len && data[i] >= 0x20 && data[i] <= 0x2F)
            {
                intermediate += data[i];
                i++;
            }

            if (i >= len)
            {
                // Incomplete CSI sequence — save for next chunk
                _leftoverStr = data.Substring(start - 2); // include ESC[
                return len;
            }

            char finalChar = data[i];

            if (prefix.Length > 0)
                ProcessCsiPrivate(prefix, paramStr, finalChar, intermediate);
            else if (intermediate.Length > 0)
                ProcessCsiIntermediate(paramStr, intermediate, finalChar);
            else
                ProcessCsiSequence(paramStr, finalChar);

            return i + 1;
        }

        /// <summary>Parse an OSC sequence starting after ESC]. Returns next index.</summary>
        private int ParseOsc(string data, int start)
        {
            int i = start;
            int len = data.Length;

            // Find terminator: BEL (\x07) or ST (ESC \)
            int payloadStart = start;
            int payloadEnd = -1;
            while (i < len)
            {
                if (data[i] == '\x07')
                {
                    payloadEnd = i;
                    ProcessOscPayload(data.Substring(payloadStart, payloadEnd - payloadStart));
                    return i + 1; // skip BEL
                }
                if (data[i] == '\x1b' && i + 1 < len && data[i + 1] == '\\')
                {
                    payloadEnd = i;
                    ProcessOscPayload(data.Substring(payloadStart, payloadEnd - payloadStart));
                    return i + 2; // skip ST
                }
                i++;
            }

            // Incomplete OSC — save for next chunk
            _leftoverStr = data.Substring(start - 2);
            return len;
        }

        /// <summary>Process the content of an OSC sequence (after ESC] and before terminator).</summary>
        private void ProcessOscPayload(string payload)
        {
            // OSC format: "code;data" or just "code"
            int semi = payload.IndexOf(';');
            if (semi < 0) return;

            if (!int.TryParse(payload.Substring(0, semi), out int code)) return;
            string oscData = payload.Substring(semi + 1);

            switch (code)
            {
                case 0: // Set window title + icon name
                case 1: // Set icon name
                case 2: // Set window title
                    TitleChanged?.Invoke(oscData);
                    break;
                case 10: // Query/set foreground color
                    if (oscData == "?")
                        UserInput?.Invoke("\x1b]10;rgb:cccc/cccc/cccc\x1b\\");
                    break;
                case 11: // Query/set background color
                    if (oscData == "?")
                        UserInput?.Invoke("\x1b]11;rgb:0c0c/0c0c/0c0c\x1b\\");
                    break;
                case 12: // Query/set cursor color
                    if (oscData == "?")
                        UserInput?.Invoke("\x1b]12;rgb:cccc/cccc/cccc\x1b\\");
                    break;
            }
        }

        /// <summary>Handle single-character ESC sequences (ESC X).</summary>
        private void ProcessEscChar(char c)
        {
            switch (c)
            {
                case 'D': // IND — Index (move cursor down, scroll if needed)
                    _buffer.IndexDown();
                    break;
                case 'M': // RI — Reverse Index (move cursor up, scroll down if needed)
                    _buffer.ReverseIndex();
                    break;
                case 'E': // NEL — Next Line
                    _buffer.CarriageReturn();
                    _buffer.IndexDown();
                    break;
                case '7': // DECSC — Save Cursor
                    _buffer.SaveCursor();
                    break;
                case '8': // DECRC — Restore Cursor
                    _buffer.RestoreCursor();
                    break;
                case 'c': // RIS — Full Reset
                    _buffer.FullReset();
                    break;
                case 'H': // HTS — Set Tab Stop at current column
                    _buffer.SetTabStop();
                    break;
                case '=': // DECKPAM — Application Keypad Mode (ignored)
                case '>': // DECKPNM — Numeric Keypad Mode (ignored)
                    break;
            }
        }

        /// <summary>Handle ESC # sequences.</summary>
        private void ProcessEscHash(char c)
        {
            switch (c)
            {
                case '8': // DECALN — Screen Alignment Pattern (fill with E)
                    _buffer.FillWithE();
                    break;
            }
        }

        /// <summary>Handle control characters (0x00–0x1F, 0x7F).</summary>
        private void ProcessControlChar(char c)
        {
            switch (c)
            {
                case '\n': // LF
                case '\x0b': // VT (vertical tab — treated as LF)
                case '\x0c': // FF (form feed — treated as LF)
                    _buffer.LineFeed();
                    break;
                case '\r': // CR
                    _buffer.CarriageReturn();
                    break;
                case '\x08': // BS
                    _buffer.Backspace();
                    break;
                case '\t': // HT
                    _buffer.Tab();
                    break;
                case '\x07': // BEL
                    _buffer.Bell();
                    BellRung?.Invoke();
                    break;
                // Other control characters are ignored
            }
        }

        /// <summary>Dispatch standard CSI sequences (no prefix).</summary>
        private void ProcessCsiSequence(string paramStr, char finalChar)
        {
            int[] parms = ParseParams(paramStr);

            switch (finalChar)
            {
                case 'm': // SGR — Select Graphic Rendition
                    ProcessSgr(parms);
                    break;

                case 'A': // CUU — Cursor Up
                    _buffer.CursorUp(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'B': // CUD — Cursor Down
                    _buffer.CursorDown(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'C': // CUF — Cursor Forward
                    _buffer.CursorForward(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'D': // CUB — Cursor Backward
                    _buffer.CursorBackward(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'E': // CNL — Cursor Next Line
                    _buffer.CursorNextLine(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'F': // CPL — Cursor Previous Line
                    _buffer.CursorPreviousLine(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'G': // CHA — Cursor Character Absolute (column)
                    _buffer.CursorToColumn(Param(parms, 0, 1) - 1); // 1-based → 0-based
                    break;
                case 'H': // CUP — Cursor Position
                case 'f': // HVP — Horizontal and Vertical Position
                    _buffer.SetCursorPosition(
                        Param(parms, 0, 1) - 1, // row: 1-based → 0-based
                        Param(parms, 1, 1) - 1); // col: 1-based → 0-based
                    break;
                case 'J': // ED — Erase in Display
                    _buffer.EraseDisplay(Param(parms, 0, 0));
                    break;
                case 'K': // EL — Erase in Line
                    _buffer.EraseLine(Param(parms, 0, 0));
                    break;
                case 'L': // IL — Insert Lines
                    _buffer.InsertLines(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'M': // DL — Delete Lines
                    _buffer.DeleteLines(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'P': // DCH — Delete Characters
                    _buffer.DeleteCharacters(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'S': // SU — Scroll Up
                    _buffer.ScrollUp(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'T': // SD — Scroll Down
                    _buffer.ScrollDown(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'X': // ECH — Erase Characters
                    _buffer.EraseCharacters(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case '@': // ICH — Insert Characters
                    _buffer.InsertCharacters(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'd': // VPA — Vertical Position Absolute (row)
                    _buffer.CursorToRow(Param(parms, 0, 1) - 1); // 1-based → 0-based
                    break;
                case 'r': // DECSTBM — Set Scrolling Region
                    {
                        int top = Param(parms, 0, 1) - 1;
                        int bottom = Param(parms, 1, _buffer.Rows) - 1;
                        _buffer.SetScrollRegion(top, bottom);
                    }
                    break;
                case 's': // Save Cursor Position
                    _buffer.SaveCursor();
                    break;
                case 'u': // Restore Cursor Position
                    _buffer.RestoreCursor();
                    break;
                case 'n': // DSR — Device Status Report
                    if (Param(parms, 0, 0) == 6)
                    {
                        // Cursor position report: ESC[row;colR
                        string report = $"\x1b[{_buffer.CursorRow + 1};{_buffer.CursorCol + 1}R";
                        UserInput?.Invoke(report);
                    }
                    break;
                case 'c': // DA — Device Attributes
                    UserInput?.Invoke("\x1b[?1;2c"); // VT100 with advanced video
                    break;
                case 'b': // REP — Repeat preceding character
                    _buffer.RepeatLastChar(Math.Max(1, Param(parms, 0, 1)));
                    break;
                case 'g': // TBC — Tab Clear
                    {
                        int mode = Param(parms, 0, 0);
                        if (mode == 0) _buffer.ClearTabStop();
                        else if (mode == 3) _buffer.ClearAllTabStops();
                    }
                    break;
                case 'h': // SM — Set Mode (standard, not DEC private)
                    for (int j = 0; j < parms.Length; j++)
                    {
                        if (parms[j] == 4) _buffer.InsertMode = true;
                    }
                    break;
                case 'l': // RM — Reset Mode (standard, not DEC private)
                    for (int j = 0; j < parms.Length; j++)
                    {
                        if (parms[j] == 4) _buffer.InsertMode = false;
                    }
                    break;
            }
        }

        /// <summary>Dispatch CSI sequences with a private prefix (?, >, !).</summary>
        private void ProcessCsiPrivate(string prefix, string paramStr, char finalChar, string intermediate = "")
        {
            int[] parms = ParseParams(paramStr);

            if (prefix == "?")
            {
                switch (finalChar)
                {
                    case 'h': // DECSET
                        for (int j = 0; j < parms.Length; j++) DecSet(parms[j]);
                        break;
                    case 'l': // DECRST
                        for (int j = 0; j < parms.Length; j++) DecReset(parms[j]);
                        break;
                    case 's': // Save DEC private modes
                        for (int j = 0; j < parms.Length; j++)
                            _buffer.SaveDecMode(parms[j], _buffer.GetDecMode(parms[j]));
                        break;
                    case 'r': // Restore DEC private modes
                        for (int j = 0; j < parms.Length; j++)
                        {
                            var saved = _buffer.RestoreDecMode(parms[j]);
                            if (saved.HasValue)
                            {
                                if (saved.Value) DecSet(parms[j]);
                                else DecReset(parms[j]);
                            }
                        }
                        break;
                }
            }
            else if (prefix == ">")
            {
                // Secondary DA (CSI > c) — report terminal type/version
                if (finalChar == 'c')
                    UserInput?.Invoke("\x1b[>0;0;0c"); // Generic VT100 compatible
            }
            else if (prefix == "!")
            {
                // DECSTR — Soft Reset (CSI ! p)
                if (finalChar == 'p')
                    _buffer.SoftReset();
            }
        }

        /// <summary>Dispatch CSI sequences with intermediate bytes (e.g., CSI n SP q).</summary>
        private void ProcessCsiIntermediate(string paramStr, string intermediate, char finalChar)
        {
            int[] parms = ParseParams(paramStr);

            if (intermediate == " " && finalChar == 'q')
            {
                // DECSCUSR — Set Cursor Shape
                _buffer.CursorShape = Param(parms, 0, 0);
            }
            else if (intermediate == "$")
            {
                // Selective erase sequences
                switch (finalChar)
                {
                    case 'J': // DECSED — Selective Erase in Display
                        _buffer.EraseDisplay(Param(parms, 0, 0));
                        break;
                    case 'K': // DECSEL — Selective Erase in Line
                        _buffer.EraseLine(Param(parms, 0, 0));
                        break;
                }
            }
        }

        /// <summary>DECSET — enable private mode.</summary>
        private void DecSet(int mode)
        {
            switch (mode)
            {
                case 1: // Application Cursor Keys
                    _buffer.ApplicationCursorKeys = true;
                    break;
                case 6: // Origin Mode (DECOM)
                    _buffer.OriginMode = true;
                    _buffer.SetCursorPosition(0, 0);
                    break;
                case 7: // Auto-wrap
                    _buffer.AutoWrap = true;
                    break;
                case 9: // X10 mouse reporting
                    _buffer.MouseMode = 9;
                    break;
                case 12: // Start blinking cursor (ignored)
                    break;
                case 25: // Show cursor
                    _buffer.CursorVisible = true;
                    break;
                case 47: // Use Alternate Screen Buffer (old)
                    _buffer.SwitchToAlternateBuffer();
                    break;
                case 1000: // Normal mouse tracking
                    _buffer.MouseMode = 1000;
                    break;
                case 1002: // Button-event mouse tracking
                    _buffer.MouseMode = 1002;
                    break;
                case 1003: // Any-event mouse tracking
                    _buffer.MouseMode = 1003;
                    break;
                case 1004: // Focus reporting
                    _buffer.FocusReporting = true;
                    break;
                case 1006: // SGR mouse encoding
                    _buffer.MouseEncoding = 1006;
                    break;
                case 1015: // URXVT mouse encoding
                    _buffer.MouseEncoding = 1015;
                    break;
                case 1047: // Use Alternate Screen Buffer
                    _buffer.SwitchToAlternateBuffer();
                    break;
                case 1048: // Save Cursor
                    _buffer.SaveCursor();
                    break;
                case 1049: // Save Cursor + Alternate Buffer (the main one vim/nano use)
                    _buffer.SaveCursor();
                    _buffer.SwitchToAlternateBuffer();
                    break;
                case 2004: // Enable Bracketed Paste Mode
                    _buffer.BracketedPasteMode = true;
                    break;
                case 2026: // Synchronized output
                    _buffer.SynchronizedOutput = true;
                    break;
            }
        }

        /// <summary>DECRST — disable private mode.</summary>
        private void DecReset(int mode)
        {
            switch (mode)
            {
                case 1: // Normal Cursor Keys
                    _buffer.ApplicationCursorKeys = false;
                    break;
                case 6: // Origin Mode off
                    _buffer.OriginMode = false;
                    _buffer.SetCursorPosition(0, 0);
                    break;
                case 7: // No auto-wrap
                    _buffer.AutoWrap = false;
                    break;
                case 9: // X10 mouse reporting off
                    if (_buffer.MouseMode == 9) _buffer.MouseMode = 0;
                    break;
                case 12: // Stop blinking cursor (ignored)
                    break;
                case 25: // Hide cursor
                    _buffer.CursorVisible = false;
                    break;
                case 47: // Use Normal Screen Buffer (old)
                    _buffer.SwitchToNormalBuffer();
                    break;
                case 1000: // Normal mouse tracking off
                    if (_buffer.MouseMode == 1000) _buffer.MouseMode = 0;
                    break;
                case 1002: // Button-event mouse tracking off
                    if (_buffer.MouseMode == 1002) _buffer.MouseMode = 0;
                    break;
                case 1003: // Any-event mouse tracking off
                    if (_buffer.MouseMode == 1003) _buffer.MouseMode = 0;
                    break;
                case 1004: // Focus reporting off
                    _buffer.FocusReporting = false;
                    break;
                case 1006: // SGR mouse encoding off
                    if (_buffer.MouseEncoding == 1006) _buffer.MouseEncoding = 0;
                    break;
                case 1015: // URXVT mouse encoding off
                    if (_buffer.MouseEncoding == 1015) _buffer.MouseEncoding = 0;
                    break;
                case 1047: // Use Normal Screen Buffer
                    _buffer.SwitchToNormalBuffer();
                    break;
                case 1048: // Restore Cursor
                    _buffer.RestoreCursor();
                    break;
                case 1049: // Restore Cursor + Normal Buffer
                    _buffer.SwitchToNormalBuffer();
                    _buffer.RestoreCursor();
                    break;
                case 2004: // Disable Bracketed Paste Mode
                    _buffer.BracketedPasteMode = false;
                    break;
                case 2026: // Synchronized output off — flush pending render
                    _buffer.SynchronizedOutput = false;
                    break;
            }
        }

        // ── SGR Processing ───────────────────────────────────────────────────

        private void ProcessSgr(int[] codes)
        {
            if (codes.Length == 0)
            {
                _buffer.CurrentStyle.Reset();
                return;
            }

            for (int i = 0; i < codes.Length; i++)
            {
                int code = codes[i];

                switch (code)
                {
                    case 0: _buffer.CurrentStyle.Reset(); break;
                    case 1: _buffer.CurrentStyle.IsBold = true; break;
                    case 2: _buffer.CurrentStyle.IsDim = true; break;
                    case 3: _buffer.CurrentStyle.IsItalic = true; break;
                    case 4: _buffer.CurrentStyle.IsUnderline = true; break;
                    case 5: _buffer.CurrentStyle.IsBlink = true; break;  // Slow blink
                    case 6: _buffer.CurrentStyle.IsBlink = true; break;  // Rapid blink (same treatment)
                    case 7: _buffer.CurrentStyle.IsInverse = true; break;
                    case 8: _buffer.CurrentStyle.IsHidden = true; break;
                    case 9: _buffer.CurrentStyle.IsStrikethrough = true; break;
                    case 21: _buffer.CurrentStyle.IsUnderline = true; break; // Double underline → underline
                    case 22: _buffer.CurrentStyle.IsBold = false; _buffer.CurrentStyle.IsDim = false; break;
                    case 23: _buffer.CurrentStyle.IsItalic = false; break;
                    case 24: _buffer.CurrentStyle.IsUnderline = false; break;
                    case 25: _buffer.CurrentStyle.IsBlink = false; break;
                    case 27: _buffer.CurrentStyle.IsInverse = false; break;
                    case 28: _buffer.CurrentStyle.IsHidden = false; break;
                    case 29: _buffer.CurrentStyle.IsStrikethrough = false; break;
                    case 39: _buffer.CurrentStyle.Foreground = null; break; // Default FG
                    case 49: _buffer.CurrentStyle.Background = null; break; // Default BG
                    case 59: _buffer.CurrentStyle.UnderlineColor = null; break; // Default underline color

                    // Foreground Standard (30-37)
                    case >= 30 and <= 37:
                        _buffer.CurrentStyle.Foreground = GetAnsiColor(code - 30, _buffer.CurrentStyle.IsBold);
                        break;
                    // Foreground Bright (90-97)
                    case >= 90 and <= 97:
                        _buffer.CurrentStyle.Foreground = GetAnsiColor(code - 90 + 8, false);
                        break;

                    // Background Standard (40-47)
                    case >= 40 and <= 47:
                        _buffer.CurrentStyle.Background = GetAnsiColor(code - 40, false);
                        break;
                    // Background Bright (100-107)
                    case >= 100 and <= 107:
                        _buffer.CurrentStyle.Background = GetAnsiColor(code - 100 + 8, false);
                        break;

                    // Extended Colors (38/48)
                    case 38: // FG
                    case 48: // BG
                        if (i + 1 < codes.Length)
                        {
                            int type = codes[i + 1];
                            if (type == 5 && i + 2 < codes.Length)
                            {
                                // 256-color: 38;5;n
                                int colorIndex = codes[i + 2];
                                var brush = GetXtermColor(colorIndex);
                                if (code == 38) _buffer.CurrentStyle.Foreground = brush;
                                else            _buffer.CurrentStyle.Background = brush;
                                i += 2;
                            }
                            else if (type == 2 && i + 4 < codes.Length)
                            {
                                // TrueColor: 38;2;r;g;b
                                byte r = (byte)Math.Clamp(codes[i + 2], 0, 255);
                                byte g = (byte)Math.Clamp(codes[i + 3], 0, 255);
                                byte b = (byte)Math.Clamp(codes[i + 4], 0, 255);
                                var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                                brush.Freeze();
                                if (code == 38) _buffer.CurrentStyle.Foreground = brush;
                                else            _buffer.CurrentStyle.Background = brush;
                                i += 4;
                            }
                        }
                        break;

                    // Underline Color (58)
                    case 58:
                        if (i + 1 < codes.Length)
                        {
                            int type = codes[i + 1];
                            if (type == 5 && i + 2 < codes.Length)
                            {
                                _buffer.CurrentStyle.UnderlineColor = GetXtermColor(codes[i + 2]);
                                i += 2;
                            }
                            else if (type == 2 && i + 4 < codes.Length)
                            {
                                byte r = (byte)Math.Clamp(codes[i + 2], 0, 255);
                                byte g = (byte)Math.Clamp(codes[i + 3], 0, 255);
                                byte b = (byte)Math.Clamp(codes[i + 4], 0, 255);
                                var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
                                brush.Freeze();
                                _buffer.CurrentStyle.UnderlineColor = brush;
                                i += 4;
                            }
                        }
                        break;
                }
            }
        }

        // ── Buffer Rendering ─────────────────────────────────────────────────

        /// <summary>
        /// Serialize the terminal buffer (scrollback + visible screen) into the
        /// AvalonEdit document and create color segments for styled cells.
        /// </summary>
        private void RenderBuffer()
        {
            var sb = new StringBuilder();
            var newSegments = new List<ColorSegment>();
            int offset = 0;

            // 1. Render scrollback (primary buffer only, not in alternate mode)
            if (!_buffer.IsAlternateBuffer)
            {
                var scrollback = _buffer.Scrollback;
                for (int i = 0; i < scrollback.Count; i++)
                {
                    var row = scrollback[i];
                    int lineStart = offset;
                    int lastNonSpace = -1;

                    for (int c = 0; c < row.Length; c++)
                    {
                        char ch = row[c].Character;
                        if (ch != ' ' && ch != '\0') lastNonSpace = c;
                    }

                    // Render up to last non-space character
                    for (int c = 0; c <= lastNonSpace; c++)
                    {
                        char ch = row[c].Character;
                        if (ch == '\0') ch = ' ';
                        sb.Append(ch);

                        if (row[c].Style.IsNonDefault)
                        {
                            AddSegment(newSegments, offset, 1, row[c].Style);
                        }
                        offset++;
                    }

                    sb.Append('\n');
                    offset++;
                }
            }

            // 2. Render visible screen
            if (_buffer.IsAlternateBuffer)
            {
                // Alternate screen (htop, nano, vim…): render ALL rows exactly so the
                // document is the same height as the terminal — no scrollbar needed.
                for (int r = 0; r < _buffer.Rows; r++)
                {
                    int lastNonSpace = -1;
                    for (int c = 0; c < _buffer.Cols; c++)
                    {
                        char ch = _buffer.GetCell(r, c).Character;
                        if (ch != ' ' && ch != '\0') lastNonSpace = c;
                    }

                    int renderTo = lastNonSpace;
                    if (r == _buffer.CursorRow)
                        renderTo = Math.Max(renderTo, _buffer.CursorCol);

                    for (int c = 0; c <= renderTo; c++)
                    {
                        var cell = _buffer.GetCell(r, c);
                        char ch = cell.Character == '\0' ? ' ' : cell.Character;
                        sb.Append(ch);
                        if (cell.Style.IsNonDefault)
                            AddSegment(newSegments, offset, 1, cell.Style);
                        offset++;
                    }

                    if (r < _buffer.Rows - 1) { sb.Append('\n'); offset++; }
                }
            }
            else
            {
                // Primary screen: only render rows up to the last row that has content
                // or holds the cursor — keeps the document compact when few lines used.
                int lastContentRow = _buffer.CursorRow;
                for (int r = _buffer.Rows - 1; r > lastContentRow; r--)
                {
                    for (int c = 0; c < _buffer.Cols; c++)
                    {
                        char ch = _buffer.GetCell(r, c).Character;
                        if (ch != ' ' && ch != '\0') { lastContentRow = r; break; }
                    }
                }

                for (int r = 0; r <= lastContentRow; r++)
                {
                    int lastNonSpace = -1;
                    for (int c = 0; c < _buffer.Cols; c++)
                    {
                        char ch = _buffer.GetCell(r, c).Character;
                        if (ch != ' ' && ch != '\0') lastNonSpace = c;
                    }

                    int renderTo = lastNonSpace;
                    if (r == _buffer.CursorRow)
                        renderTo = Math.Max(renderTo, _buffer.CursorCol);

                    for (int c = 0; c <= renderTo; c++)
                    {
                        var cell = _buffer.GetCell(r, c);
                        char ch = cell.Character == '\0' ? ' ' : cell.Character;
                        sb.Append(ch);
                        if (cell.Style.IsNonDefault)
                            AddSegment(newSegments, offset, 1, cell.Style);
                        offset++;
                    }

                    if (r < lastContentRow) { sb.Append('\n'); offset++; }
                }
            }

            // 3. Update document
            Document.BeginUpdate();
            try
            {
                _segments.Clear();
                Document.Text = sb.ToString();

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

            // Hide scrollbar in alternate-buffer mode (htop, nano, vim…) — those apps
            // own the full screen and there is nothing to scroll. Show it in primary
            // mode so scrollback history is reachable.
            SetInternalScrollBarVisibility(_buffer.IsAlternateBuffer
                ? ScrollBarVisibility.Hidden
                : ScrollBarVisibility.Auto);

            // 4. Position caret and scroll
            PositionCaret();
        }

        private void PositionCaret()
        {
            // Calculate caret offset: count through scrollback + visible rows
            int targetOffset = 0;

            if (!_buffer.IsAlternateBuffer)
            {
                // Skip past scrollback lines
                var scrollback = _buffer.Scrollback;
                for (int i = 0; i < scrollback.Count; i++)
                {
                    var row = scrollback[i];
                    int lastNonSpace = -1;
                    for (int c = 0; c < row.Length; c++)
                    {
                        if (row[c].Character != ' ' && row[c].Character != '\0')
                            lastNonSpace = c;
                    }
                    targetOffset += lastNonSpace + 1; // chars on this line
                    targetOffset++; // newline
                }
            }

            // Walk through screen rows to the cursor row
            for (int r = 0; r < _buffer.CursorRow; r++)
            {
                int lastNonSpace = -1;
                for (int c = 0; c < _buffer.Cols; c++)
                {
                    char ch = _buffer.GetCell(r, c).Character;
                    if (ch != ' ' && ch != '\0') lastNonSpace = c;
                }
                int renderTo = lastNonSpace;
                if (r == _buffer.CursorRow)
                    renderTo = Math.Max(renderTo, _buffer.CursorCol);
                targetOffset += renderTo + 1; // chars
                targetOffset++; // newline
            }

            // Add cursor column
            targetOffset += _buffer.CursorCol;

            targetOffset = Math.Clamp(targetOffset, 0, Document.TextLength);

            TextArea.Caret.Offset = targetOffset;

            // Cursor visibility: hide/show caret based on buffer flag
            TextArea.Caret.CaretBrush = _buffer.CursorVisible ? TerminalCaret : TerminalCaretHidden;

            // Scroll so the caret (cursor) is always visible — this keeps the active
            // line in view whether we're mid-screen (password prompt) or at the bottom
            // of a full scrollback history.
            TextArea.Caret.BringCaretToView();
        }

        private ScrollBarVisibility _currentScrollBarVisibility = ScrollBarVisibility.Hidden;

        /// <summary>
        /// Walk the visual tree to find AvalonEdit's internal ScrollViewer and toggle
        /// the vertical scrollbar visibility. Called every render to track buffer mode.
        /// </summary>
        private void SetInternalScrollBarVisibility(ScrollBarVisibility visibility)
        {
            if (_currentScrollBarVisibility == visibility) return;
            _currentScrollBarVisibility = visibility;

            // AvalonEdit's TextEditor template contains a ScrollViewer named PART_ScrollViewer.
            // Walk the immediate visual children to find it.
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(this); i++)
            {
                if (VisualTreeHelper.GetChild(this, i) is ScrollViewer sv)
                {
                    sv.VerticalScrollBarVisibility = visibility;
                    return;
                }
            }
            // Fallback: set the attached property (works if the template reads it)
            ScrollViewer.SetVerticalScrollBarVisibility(this, visibility);
        }

        private static void AddSegment(List<ColorSegment> segments, int offset, int length,
            TerminalBuffer.CellStyle style)
        {
            var fg = style.Foreground;
            var bg = style.Background;

            // Handle inverse (swap FG and BG)
            if (style.IsInverse)
            {
                (fg, bg) = (bg ?? TerminalBackground, fg ?? TerminalForeground);
            }

            // Handle hidden text: set foreground = background
            if (style.IsHidden)
            {
                fg = bg ?? TerminalBackground;
            }

            segments.Add(new ColorSegment
            {
                StartOffset = offset,
                Length = length,
                Foreground = fg,
                Background = bg,
                IsBold = style.IsBold || style.IsBlink, // Map blink to bold for visual effect
                IsDim = style.IsDim,
                IsItalic = style.IsItalic,
                IsUnderline = style.IsUnderline,
                IsStrikethrough = style.IsStrikethrough,
                IsBlink = style.IsBlink,
                IsHidden = style.IsHidden,
                UnderlineColor = style.UnderlineColor,
            });
        }

        // ── Param helpers ────────────────────────────────────────────────────

        private static int[] ParseParams(string paramStr)
        {
            if (string.IsNullOrEmpty(paramStr))
                return Array.Empty<int>();

            var parts = paramStr.Split(';');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out result[i]))
                    result[i] = 0;
            }
            return result;
        }

        /// <summary>Get parameter at index, or default if missing.</summary>
        private static int Param(int[] parms, int index, int defaultValue)
        {
            if (index >= parms.Length || parms[index] == 0) return defaultValue;
            return parms[index];
        }

        // ── Input Handling ──────────────────────────────────────────────────

        private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_hiddenInputCallback != null)
            {
                // Hidden mode: accumulate printable chars without echoing them
                foreach (char ch in e.Text)
                    if (ch >= ' ')
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
                    if (_buffer.BracketedPasteMode)
                        UserInput?.Invoke($"\x1b[200~{text}\x1b[201~");
                    else
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

        // ── Color data structures ────────────────────────────────────────────

        private class ColorSegment : TextSegment
        {
            public Brush? Foreground;
            public Brush? Background;
            public bool IsBold;
            public bool IsDim;
            public bool IsItalic;
            public bool IsUnderline;
            public bool IsStrikethrough;
            public bool IsBlink;
            public bool IsHidden;
            public Brush? UnderlineColor;
        }

        private class AnsiColorizer : DocumentColorizingTransformer
        {
            private readonly TextSegmentCollection<ColorSegment> _segments;

            public AnsiColorizer(TextSegmentCollection<ColorSegment> segments)
            {
                _segments = segments;
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
                            if (seg.Foreground != null)
                                element.TextRunProperties.SetForegroundBrush(seg.Foreground);
                            if (seg.Background != null)
                                element.TextRunProperties.SetBackgroundBrush(seg.Background);

                            if (seg.IsBold || seg.IsItalic)
                            {
                                var tf = element.TextRunProperties.Typeface;
                                var weight = seg.IsBold ? FontWeights.Bold : tf.Weight;
                                var style = seg.IsItalic ? FontStyles.Italic : tf.Style;
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    tf.FontFamily, style, weight, tf.Stretch));
                            }
                            if (seg.IsUnderline || seg.IsStrikethrough)
                            {
                                var decorations = new TextDecorationCollection();
                                if (seg.IsUnderline)
                                {
                                    if (seg.UnderlineColor != null)
                                    {
                                        var pen = new Pen(seg.UnderlineColor, 1);
                                        pen.Freeze();
                                        decorations.Add(new TextDecoration(TextDecorationLocation.Underline, pen, 0, TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended));
                                    }
                                    else
                                    {
                                        decorations.Add(TextDecorations.Underline);
                                    }
                                }
                                if (seg.IsStrikethrough) decorations.Add(TextDecorations.Strikethrough);
                                element.TextRunProperties.SetTextDecorations(decorations);
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
            new SolidColorBrush(Color.FromRgb(52, 101, 164)), // 4 Blue
            new SolidColorBrush(Color.FromRgb(117, 80, 123)), // 5 Magenta
            new SolidColorBrush(Color.FromRgb(6, 152, 154)),  // 6 Cyan
            new SolidColorBrush(Color.FromRgb(211, 215, 207)),// 7 White

            // 8-15: Bright (Bold)
            new SolidColorBrush(Color.FromRgb(85, 87, 83)),   // 8 Bright Black
            new SolidColorBrush(Color.FromRgb(239, 41, 41)),  // 9 Bright Red
            new SolidColorBrush(Color.FromRgb(138, 226, 52)), // 10 Bright Green
            new SolidColorBrush(Color.FromRgb(252, 233, 79)), // 11 Bright Yellow
            new SolidColorBrush(Color.FromRgb(114, 159, 207)),// 12 Bright Blue
            new SolidColorBrush(Color.FromRgb(173, 127, 168)),// 13 Bright Magenta
            new SolidColorBrush(Color.FromRgb(52, 226, 226)), // 14 Bright Cyan
            new SolidColorBrush(Color.FromRgb(238, 238, 236)),// 15 Bright White
        };

        private static Brush GetAnsiColor(int index, bool isBold)
        {
            if (isBold && index < 8) index += 8;
            if (index >= 0 && index < AnsiColors.Length) return AnsiColors[index];
            return AnsiColors[7];
        }

        private static readonly ConcurrentDictionary<int, Brush> XtermCache = new();

        private static Brush GetXtermColor(int index)
        {
            if (index < 0 || index > 255) return AnsiColors[7];
            if (index < 16) return AnsiColors[index];

            return XtermCache.GetOrAdd(index, idx =>
            {
                Color c;
                if (idx >= 16 && idx <= 231)
                {
                    int val = idx - 16;
                    int b = val % 6; val /= 6;
                    int g = val % 6; val /= 6;
                    int r = val;
                    byte ByteMap(int v) => (byte)(v == 0 ? 0 : (v * 40 + 55));
                    c = Color.FromRgb(ByteMap(r), ByteMap(g), ByteMap(b));
                }
                else
                {
                    int gray = (idx - 232) * 10 + 8;
                    c = Color.FromRgb((byte)gray, (byte)gray, (byte)gray);
                }
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            });
        }

        // ── Key mapping ──────────────────────────────────────────────────────

        private string? KeyToSequence(KeyEventArgs e)
        {
            bool ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;

            // Application cursor keys mode changes arrow sequences
            bool appCursor = _buffer.ApplicationCursorKeys;

            switch (e.Key)
            {
                case Key.Enter:     return "\r";
                case Key.Space:     return " ";
                case Key.Back:      return "\x7f";
                case Key.Tab:
                    return shift ? "\x1b[Z" : "\t"; // Shift+Tab → reverse tab
                case Key.Escape:    return "\x1b";
                case Key.Up:        return appCursor ? "\x1bOA" : "\x1b[A";
                case Key.Down:      return appCursor ? "\x1bOB" : "\x1b[B";
                case Key.Right:     return appCursor ? "\x1bOC" : "\x1b[C";
                case Key.Left:      return appCursor ? "\x1bOD" : "\x1b[D";
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

        // ── Dynamic Resize ───────────────────────────────────────────────────

        /// <summary>
        /// Snap the arranged height to a whole number of character lines so that
        /// no text line is ever half-visible during window resize.
        /// </summary>
        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            if (!_charSizeMeasured) MeasureCharSize();

            if (_charSizeMeasured && _charHeight > 0)
            {
                double vertPad   = Padding.Top + Padding.Bottom;
                double available = arrangeBounds.Height - vertPad;
                int    lines     = Math.Max(1, (int)(available / _charHeight));
                double snapped   = lines * _charHeight + vertPad;

                // Only snap if it meaningfully differs to avoid infinite layout loops
                if (Math.Abs(snapped - arrangeBounds.Height) > 0.5)
                    arrangeBounds = new Size(arrangeBounds.Width, snapped);
            }

            return base.ArrangeOverride(arrangeBounds);
        }

        private void MeasureCharSize()
        {
            var tf = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText("W",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, FontSize, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            _charWidth = ft.WidthIncludingTrailingWhitespace;
            _charHeight = ft.Height;
            _charSizeMeasured = _charWidth > 0 && _charHeight > 0;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_charSizeMeasured) MeasureCharSize();
            if (!_charSizeMeasured) return;

            double availableWidth  = ActualWidth  - Padding.Left - Padding.Right;
            double availableHeight = ActualHeight - Padding.Top  - Padding.Bottom;

            int newCols = Math.Max(1, (int)(availableWidth / _charWidth));
            int newRows = Math.Max(1, (int)(availableHeight / _charHeight));

            if (newCols != _buffer.Cols || newRows != _buffer.Rows)
            {
                _buffer.Resize(newRows, newCols);
                if (_buffer.IsDirty)
                {
                    RenderBuffer();
                    _buffer.IsDirty = false;
                }
                TerminalResized?.Invoke((uint)newCols, (uint)newRows);
            }
        }

        // ── Mouse Reporting ──────────────────────────────────────────────────

        /// <summary>Convert a mouse position (WPF point) to terminal row/col (1-based).</summary>
        private (int col, int row) MousePositionToCell(Point pos)
        {
            if (!_charSizeMeasured) MeasureCharSize();
            if (!_charSizeMeasured) return (1, 1);

            int col = Math.Max(1, (int)((pos.X - Padding.Left) / _charWidth) + 1);
            int row = Math.Max(1, (int)((pos.Y - Padding.Top) / _charHeight) + 1);
            return (col, row);
        }

        private void SendMouseEvent(int button, int col, int row, bool release)
        {
            if (_buffer.MouseEncoding == 1006)
            {
                // SGR format: ESC[<button;col;rowM (press) or ESC[<button;col;rowm (release)
                char suffix = release ? 'm' : 'M';
                UserInput?.Invoke($"\x1b[<{button};{col};{row}{suffix}");
            }
            else if (_buffer.MouseEncoding == 1015)
            {
                // URXVT format: ESC[button;col;rowM
                UserInput?.Invoke($"\x1b[{button + 32};{col};{row}M");
            }
            else
            {
                // X10/normal format: ESC[M Cb Cx Cy (encoded as char + 32)
                if (col <= 223 && row <= 223) // limit for traditional encoding
                {
                    int cb = button + 32;
                    UserInput?.Invoke($"\x1b[M{(char)cb}{(char)(col + 32)}{(char)(row + 32)}");
                }
            }
        }

        private int MouseButtonNumber(MouseButton button)
        {
            return button switch
            {
                MouseButton.Left => 0,
                MouseButton.Middle => 1,
                MouseButton.Right => 2,
                _ => 0
            };
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_buffer.MouseMode == 0) return;
            var pos = e.GetPosition(TextArea.TextView);
            var (col, row) = MousePositionToCell(pos);
            int btn = MouseButtonNumber(e.ChangedButton);

            // Add modifier bits
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) btn += 4;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) btn += 8;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) btn += 16;

            SendMouseEvent(btn, col, row, false);
            e.Handled = true;
        }

        private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_buffer.MouseMode == 0) return;
            if (_buffer.MouseMode == 9) return; // X10 mode doesn't report releases

            var pos = e.GetPosition(TextArea.TextView);
            var (col, row) = MousePositionToCell(pos);

            if (_buffer.MouseEncoding == 1006)
            {
                // SGR: report actual button number on release
                int btn = MouseButtonNumber(e.ChangedButton);
                SendMouseEvent(btn, col, row, true);
            }
            else
            {
                // Normal/URXVT: release is button 3
                SendMouseEvent(3, col, row, false);
            }
            e.Handled = true;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Only report motion for modes 1002 (button-event) and 1003 (any-event)
            if (_buffer.MouseMode < 1002) return;
            if (_buffer.MouseMode == 1002 && e.LeftButton == MouseButtonState.Released &&
                e.MiddleButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released)
                return; // 1002 only reports motion while a button is pressed

            var pos = e.GetPosition(TextArea.TextView);
            var (col, row) = MousePositionToCell(pos);

            int btn = 32; // motion flag
            if (e.LeftButton == MouseButtonState.Pressed) btn += 0;
            else if (e.MiddleButton == MouseButtonState.Pressed) btn += 1;
            else if (e.RightButton == MouseButtonState.Pressed) btn += 2;
            else btn += 3; // no button (any-event mode)

            SendMouseEvent(btn, col, row, false);
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_buffer.MouseMode == 0) return;

            var pos = e.GetPosition(TextArea.TextView);
            var (col, row) = MousePositionToCell(pos);
            int btn = e.Delta > 0 ? 64 : 65; // scroll up or down
            SendMouseEvent(btn, col, row, false);
            e.Handled = true;
        }
    }
}
