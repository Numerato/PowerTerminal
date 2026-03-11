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

        // Queue for background appending to avoid UI freeze
        private readonly ConcurrentQueue<string> _incomingDataQueue = new();
        private string _leftoverStr = "";
        private int _isRendering;

        private static readonly SolidColorBrush TerminalForeground = new(Color.FromRgb(204, 204, 204));
        private static readonly SolidColorBrush TerminalBackground = new(Color.FromRgb(12, 12, 12));
        private static readonly SolidColorBrush TerminalCaret      = new(Color.FromRgb(204, 204, 204));
        private static readonly SolidColorBrush TerminalSelection  = new(Color.FromArgb(120, 92, 40, 0));

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
                Dispatcher.InvokeAsync(ProcessQueue, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        // ── Escape Sequence Parser (character-by-character) ──────────────────

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
                        Dispatcher.InvokeAsync(ProcessQueue, System.Windows.Threading.DispatcherPriority.Normal);
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
                        // Incomplete escape — save for next chunk
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

            if (i >= len)
            {
                // Incomplete CSI sequence — save for next chunk
                _leftoverStr = data.Substring(start - 2); // include ESC[
                return len;
            }

            char finalChar = data[i];
            string paramStr = data.Substring(paramStart, i - paramStart);

            if (prefix.Length > 0)
                ProcessCsiPrivate(prefix, paramStr, finalChar);
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
            while (i < len)
            {
                if (data[i] == '\x07')
                    return i + 1; // skip BEL
                if (data[i] == '\x1b' && i + 1 < len && data[i + 1] == '\\')
                    return i + 2; // skip ST
                i++;
            }

            // Incomplete OSC — save for next chunk
            _leftoverStr = data.Substring(start - 2);
            return len;
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
                case 'H': // HTS — Set Tab Stop (ignored)
                    break;
                case '=': // DECKPAM — Application Keypad Mode (ignored)
                case '>': // DECKPNM — Numeric Keypad Mode (ignored)
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
                    // Ignored for simplicity
                    break;
            }
        }

        /// <summary>Dispatch CSI sequences with a private prefix (?, >, !).</summary>
        private void ProcessCsiPrivate(string prefix, string paramStr, char finalChar)
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
                }
            }
            // Other private prefixes (>, !) are ignored
        }

        /// <summary>DECSET — enable private mode.</summary>
        private void DecSet(int mode)
        {
            switch (mode)
            {
                case 1: // Application Cursor Keys
                    _buffer.ApplicationCursorKeys = true;
                    break;
                case 7: // Auto-wrap
                    _buffer.AutoWrap = true;
                    break;
                case 12: // Start blinking cursor (ignored)
                    break;
                case 25: // Show cursor
                    _buffer.CursorVisible = true;
                    break;
                case 47: // Use Alternate Screen Buffer (old)
                    _buffer.SwitchToAlternateBuffer();
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
                case 7: // No auto-wrap
                    _buffer.AutoWrap = false;
                    break;
                case 12: // Stop blinking cursor (ignored)
                    break;
                case 25: // Hide cursor
                    _buffer.CursorVisible = false;
                    break;
                case 47: // Use Normal Screen Buffer (old)
                    _buffer.SwitchToNormalBuffer();
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
                    case 7: _buffer.CurrentStyle.IsInverse = true; break;
                    case 9: _buffer.CurrentStyle.IsStrikethrough = true; break;
                    case 21: _buffer.CurrentStyle.IsUnderline = true; break; // Double underline → underline
                    case 22: _buffer.CurrentStyle.IsBold = false; _buffer.CurrentStyle.IsDim = false; break;
                    case 23: _buffer.CurrentStyle.IsItalic = false; break;
                    case 24: _buffer.CurrentStyle.IsUnderline = false; break;
                    case 27: _buffer.CurrentStyle.IsInverse = false; break;
                    case 29: _buffer.CurrentStyle.IsStrikethrough = false; break;
                    case 39: _buffer.CurrentStyle.Foreground = null; break; // Default FG
                    case 49: _buffer.CurrentStyle.Background = null; break; // Default BG

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
            for (int r = 0; r < _buffer.Rows; r++)
            {
                int lineStart = offset;
                int lastNonSpace = -1;

                // Find last non-space character on this row
                for (int c = 0; c < _buffer.Cols; c++)
                {
                    char ch = _buffer.GetCell(r, c).Character;
                    if (ch != ' ' && ch != '\0') lastNonSpace = c;
                }

                // Always render at least one column (even if empty) for cursor visibility
                int renderTo = lastNonSpace;

                // If cursor is on this row, extend rendering to cursor position
                if (r == _buffer.CursorRow)
                    renderTo = Math.Max(renderTo, _buffer.CursorCol);

                for (int c = 0; c <= renderTo; c++)
                {
                    var cell = _buffer.GetCell(r, c);
                    char ch = cell.Character;
                    if (ch == '\0') ch = ' ';
                    sb.Append(ch);

                    if (cell.Style.IsNonDefault)
                    {
                        AddSegment(newSegments, offset, 1, cell.Style);
                    }
                    offset++;
                }

                // Add newline for all rows except the last
                if (r < _buffer.Rows - 1)
                {
                    sb.Append('\n');
                    offset++;
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
            ScrollToEnd();
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

            segments.Add(new ColorSegment
            {
                StartOffset = offset,
                Length = length,
                Foreground = fg,
                Background = bg,
                IsBold = style.IsBold,
                IsDim = style.IsDim,
                IsItalic = style.IsItalic,
                IsUnderline = style.IsUnderline,
                IsStrikethrough = style.IsStrikethrough,
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

                            var tf = element.TextRunProperties.Typeface;

                            if (seg.IsBold)
                            {
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    tf.FontFamily, tf.Style, FontWeights.Bold, tf.Stretch));
                                tf = element.TextRunProperties.Typeface;
                            }
                            if (seg.IsItalic)
                            {
                                element.TextRunProperties.SetTypeface(new Typeface(
                                    tf.FontFamily, FontStyles.Italic, tf.Weight, tf.Stretch));
                            }
                            if (seg.IsUnderline || seg.IsStrikethrough)
                            {
                                var decorations = new TextDecorationCollection();
                                if (seg.IsUnderline) decorations.Add(TextDecorations.Underline);
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
    }
}
