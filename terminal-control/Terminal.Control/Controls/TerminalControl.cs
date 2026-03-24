namespace Terminal.Controls;

using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Terminal.Vt;
using Terminal.Ssh;

/// <summary>Controls how copy and paste work in the terminal panel.</summary>
public enum TerminalCopyPasteMode
{
    /// <summary>Left-drag selects and auto-copies to clipboard; right-click pastes immediately.</summary>
    Classic,
    /// <summary>Right-click opens a popup menu with Copy and Paste items (default).</summary>
    RightClickMenu
}

public sealed class TerminalControl : FrameworkElement
{
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TerminalControl),
            new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(TerminalControl),
            new FrameworkPropertyMetadata(new FontFamily("Consolas"), FrameworkPropertyMetadataOptions.AffectsRender, OnFontChanged));

    public static readonly DependencyProperty BackgroundColorProperty =
        DependencyProperty.Register(nameof(BackgroundColor), typeof(Color), typeof(TerminalControl),
            new FrameworkPropertyMetadata(Color.FromRgb(0x30, 0x0A, 0x24), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ServerNameProperty =
        DependencyProperty.Register(nameof(ServerName), typeof(string), typeof(TerminalControl),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty UsernameProperty =
        DependencyProperty.Register(nameof(Username), typeof(string), typeof(TerminalControl),
            new FrameworkPropertyMetadata(string.Empty));

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Background fill color of the terminal (default: Ubuntu purple-black #300A24).</summary>
    public Color BackgroundColor
    {
        get => (Color)GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>SSH server name/address — can be bound to the toolbar field.</summary>
    public string ServerName
    {
        get => (string)GetValue(ServerNameProperty);
        set => SetValue(ServerNameProperty, value);
    }

    /// <summary>SSH username — can be bound to the toolbar field.</summary>
    public string Username
    {
        get => (string)GetValue(UsernameProperty);
        set => SetValue(UsernameProperty, value);
    }

    private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TerminalControl tc)
        {
            tc._glyphTypeface = null;
            tc._boldGlyphTypeface = null;
            tc.MeasureCells();
            tc.QueueRender();
        }
    }

    private TerminalEmulator? _emulator;
    private ISshTerminalSession? _session;

    private GlyphTypeface? _glyphTypeface;
    private GlyphTypeface? _boldGlyphTypeface;
    private double _cellWidth;
    private double _cellHeight;
    private double _cellBaseline;

    public int TerminalColumns { get; private set; }
    public int TerminalRows { get; private set; }

    private readonly DispatcherTimer _cursorTimer;
    private DispatcherTimer? _resizeDebounce;
    private (int cols, int rows) _pendingResize;
    private bool _cursorBlinkState = true;
    private bool _renderPending;

    private int _scrollOffset; // 0 = live bottom, positive = scrolled up

    // Password / input prompt mode
    private bool _isPasswordMode;          // masked (no echo)
    private bool _isInputMode;             // visible echo
    private readonly StringBuilder _passwordBuffer = new();
    private TaskCompletionSource<string>? _passwordTcs;

    // Text selection
    private (int row, int col)? _selStart;
    private (int row, int col)? _selEnd;
    private bool _isSelecting;

    // PowerEdit: tracks what the user is typing on the current shell line
    private readonly StringBuilder _commandBuffer = new();
    /// <summary>When true, "poweredit &lt;file&gt;" commands are intercepted before they reach the shell.</summary>
    public bool EnablePowerEdit { get; set; }
    /// <summary>Fired when a "poweredit &lt;file&gt;" command is intercepted. Arg is the full command string.</summary>
    public event EventHandler<string>? PowerEditCommand;

    /// <summary>Controls copy/paste interaction model in the terminal.</summary>
    public TerminalCopyPasteMode CopyPasteMode { get; set; } = TerminalCopyPasteMode.RightClickMenu;

    public TerminalControl()
    {
        Focusable = true;
        Cursor = System.Windows.Input.Cursors.IBeam;
        ClipToBounds = true;
        FocusVisualStyle = null;

        _cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _cursorTimer.Tick += (_, _) => { _cursorBlinkState = !_cursorBlinkState; QueueRender(); };
        _cursorTimer.Start();

        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy" };
        copyItem.Click += (_, _) => CopySelection();
        var pasteItem = new MenuItem { Header = "Paste" };
        pasteItem.Click += (_, _) => PasteFromClipboard();
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        ContextMenu = menu;
        // Suppress context menu opening when in Classic mode
        ContextMenuOpening += (_, e) => { if (CopyPasteMode == TerminalCopyPasteMode.Classic) e.Handled = true; };
    }

    public void AttachSession(ISshTerminalSession session)
    {
        if (_session != null)
        {
            _session.DataReceived -= OnDataReceived;
            _session.Disconnected -= OnDisconnected;
        }
        _session = session;
        if (_emulator == null)
        {
            _emulator = new TerminalEmulator(TerminalColumns > 0 ? TerminalColumns : 80, TerminalRows > 0 ? TerminalRows : 24);
            _emulator.TitleChanged += (_, t) => { };
            _emulator.BellRaised += (_, _) => System.Media.SystemSounds.Beep.Play();
            _emulator.CursorVisibilityChanged += (_, _) => QueueRender();
        }
        _emulator.ResponseReady += (_, response) => _session?.Send(response);
        _session.DataReceived += OnDataReceived;
        _session.Disconnected += OnDisconnected;
        _emulator.Buffer.MarkAllDirty();
        QueueRender();
    }

    protected override int VisualChildrenCount => 0;

    private void OnDataReceived(object? sender, byte[] data)
    {
        // Use DispatcherPriority.Normal (9) — same as WPF layout passes.
        // Render priority (7) is LOWER than Normal, so a resize triggered by
        // window dragging could call Resize() before queued ProcessBytes ran,
        // producing stale/blank rows in the reflow. Normal priority ensures
        // data processing and layout updates are properly sequenced.
        Dispatcher.BeginInvoke(() =>
        {
            _emulator?.ProcessBytes(data, 0, data.Length);
            _scrollOffset = 0;
            QueueRender();
            ScrollbackChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Normal);
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var msg = System.Text.Encoding.UTF8.GetBytes("\r\n[Disconnected]\r\n");
            _emulator?.ProcessBytes(msg, 0, msg.Length);
            QueueRender();
        });
    }

    private void QueueRender()
    {
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.BeginInvoke(DoRender, DispatcherPriority.Render);
    }

    private void DoRender()
    {
        _renderPending = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        EnsureCellMetrics();
        if (_emulator == null)
        {
            dc.DrawRectangle(new SolidColorBrush(BackgroundColor), null, new Rect(0, 0, ActualWidth, ActualHeight));
            return;
        }
        RenderTerminal(dc);
    }

    private void EnsureCellMetrics()
    {
        if (_glyphTypeface != null && _cellWidth > 0) return;
        MeasureCells();
    }

    private void MeasureCells()
    {
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var boldTypeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        if (!typeface.TryGetGlyphTypeface(out var gtf))
        {
            _cellWidth = FontSize * 0.6;
            _cellHeight = FontSize * 1.4;
            _cellBaseline = FontSize * 1.1;
            return;
        }

        _glyphTypeface = gtf;
        boldTypeface.TryGetGlyphTypeface(out _boldGlyphTypeface);

        double em = FontSize;
        if (gtf.CharacterToGlyphMap.TryGetValue('M', out ushort mGlyph))
            _cellWidth = Math.Ceiling(gtf.AdvanceWidths[mGlyph] * em);
        else
            _cellWidth = Math.Floor(em * 0.6);

        _cellHeight = Math.Ceiling(gtf.Height * em) + 2;
        _cellBaseline = gtf.Baseline * em + 1;
    }

    private void RenderTerminal(DrawingContext dc)
    {
        var buf = _emulator!.Buffer;
        int cols = buf.Columns;
        int rows = buf.Rows;
        double termWidth = ActualWidth;

        var defaultBgColor = TerminalColor.DefaultBg.Resolve(false, false);
        dc.DrawRectangle(new SolidColorBrush(defaultBgColor), null, new Rect(0, 0, ActualWidth, ActualHeight));

        int scrollback = buf.ScrollbackCount;

        for (int displayRow = 0; displayRow < rows; displayRow++)
        {
            double y = displayRow * _cellHeight;

            // virtualRow: position in the combined scrollback+live stream
            // virtualRow < scrollback => from scrollback; >= scrollback => from main buffer
            int virtualRow = scrollback - _scrollOffset + displayRow;

            ScreenCell[]? scrollbackRowData = null;
            int mainBufferRow = -1;

            if (virtualRow < 0)
                continue;
            else if (virtualRow < scrollback)
                scrollbackRowData = buf.GetScrollbackRow(virtualRow);
            else
                mainBufferRow = virtualRow - scrollback;

            if (mainBufferRow >= rows) continue;

            // Render backgrounds
            int bgStart = 0;
            Color? lastBg = null;

            for (int col = 0; col <= cols; col++)
            {
                Color? currentBg = null;
                if (col < cols)
                {
                    var cell = scrollbackRowData != null
                        ? (col < scrollbackRowData.Length ? scrollbackRowData[col] : ScreenCell.Empty)
                        : buf.GetCellCopy(mainBufferRow, col);
                    var (_, bg) = cell.Attributes.EffectiveColors;
                    currentBg = bg.Resolve(false, false);

                    // Apply selection highlight (only for live buffer rows)
                    if (mainBufferRow >= 0 && IsInSelection(mainBufferRow, col))
                        currentBg = Color.FromArgb(180, 51, 153, 255);
                }

                if (lastBg.HasValue && (!currentBg.HasValue || currentBg.Value != lastBg.Value))
                {
                    if (lastBg.Value != defaultBgColor)
                        dc.DrawRectangle(new SolidColorBrush(lastBg.Value), null,
                            new Rect(bgStart * _cellWidth, y, (col - bgStart) * _cellWidth, _cellHeight));
                    bgStart = col;
                }
                else if (!lastBg.HasValue)
                    bgStart = col;
                lastBg = currentBg;
            }

            // Render foreground text
            RenderRowText(dc, displayRow, y, buf, scrollbackRowData, mainBufferRow);
        }

        // Cursor (only when not scrolled up; show during password prompts too)
        if (_scrollOffset == 0 && buf.CursorVisible && (_isPasswordMode || _session?.IsConnected == true))
        {
            var style = _emulator?.CursorStyle ?? CursorStyle.BlinkingBlock;
            bool blinking = style is CursorStyle.BlinkingBlock or CursorStyle.BlinkingUnderline or CursorStyle.BlinkingBar;
            bool drawCursor = !blinking || _cursorBlinkState;

            if (drawCursor)
            {
                double cx = buf.CursorCol * _cellWidth;
                double cy = buf.CursorRow * _cellHeight;
                var cursorBrush = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200));

                if (style is CursorStyle.BlinkingBar or CursorStyle.SteadyBar)
                    dc.DrawRectangle(cursorBrush, null, new Rect(cx, cy, 2, _cellHeight));
                else if (style is CursorStyle.BlinkingUnderline or CursorStyle.SteadyUnderline)
                    dc.DrawRectangle(cursorBrush, null, new Rect(cx, cy + _cellHeight - 2, _cellWidth, 2));
                else
                {
                    dc.DrawRectangle(cursorBrush, null, new Rect(cx, cy, _cellWidth, _cellHeight));
                    var cell = buf.GetCellCopy(buf.CursorRow, buf.CursorCol);
                    if (cell.Character.Value > 32)
                    {
                        var fgBrush = new SolidColorBrush(BackgroundColor);
                        DrawGlyph(dc, cell.Character, fgBrush, cx, cy + _cellBaseline, false);
                    }
                }
            }
        }
    }

    private void RenderRowText(DrawingContext dc, int displayRow, double y, ScreenBuffer buf,
        ScreenCell[]? scrollbackRowData, int mainBufferRow)
    {
        if (_glyphTypeface == null) return;
        int cols = buf.Columns;

        // Collect runs: (startCol, endCol, color, bold, chars)
        int runStart = -1;
        Color runColor = default;
        bool runBold = false;
        var runGlyphs = new List<ushort>();
        var runAdvances = new List<double>();
        double runX = 0;

        void FlushRun()
        {
            if (runStart < 0 || runGlyphs.Count == 0) return;
            var gtf = (runBold && _boldGlyphTypeface != null) ? _boldGlyphTypeface : _glyphTypeface!;
            try
            {
                var glyphRun = new GlyphRun(
                    gtf, 0, false, FontSize,
                    (float)VisualTreeHelper.GetDpi(this).PixelsPerDip,
                    runGlyphs.ToArray(),
                    new Point(runX, y + _cellBaseline),
                    runAdvances.ToArray(),
                    null, null, null, null, null, null);
                dc.DrawGlyphRun(new SolidColorBrush(runColor), glyphRun);
            }
            catch { /* invalid glyph run, skip */ }
            runStart = -1;
            runGlyphs.Clear();
            runAdvances.Clear();
        }

        for (int col = 0; col < cols; col++)
        {
            var cell = scrollbackRowData != null
                ? (col < scrollbackRowData.Length ? scrollbackRowData[col] : ScreenCell.Empty)
                : (mainBufferRow >= 0 ? buf.GetCellCopy(mainBufferRow, col) : ScreenCell.Empty);
            if (cell.IsWideRight) continue;

            var ch = cell.Character;
            if (ch.Value <= 0x20) { FlushRun(); continue; }

            var (fg, _) = cell.Attributes.EffectiveColors;
            var fgColor = fg.Resolve(true, cell.Attributes.Bold);
            bool bold = cell.Attributes.Bold;

            // Get glyph
            var gtf = (bold && _boldGlyphTypeface != null) ? _boldGlyphTypeface! : _glyphTypeface!;
            char firstChar = ch.ToString()[0];
            if (!gtf.CharacterToGlyphMap.TryGetValue(firstChar, out ushort glyphIndex))
            {
                FlushRun();
                continue;
            }

            double advance = cell.IsWide ? _cellWidth * 2 : _cellWidth;

            // If run properties changed, flush
            if (runStart >= 0 && (fgColor != runColor || bold != runBold))
                FlushRun();

            if (runStart < 0)
            {
                runStart = col;
                runColor = fgColor;
                runBold = bold;
                runX = col * _cellWidth;
            }

            runGlyphs.Add(glyphIndex);
            runAdvances.Add(advance);

            // Decorations
            double x = col * _cellWidth;
            if (cell.Attributes.Underline)
            {
                var pen = new Pen(new SolidColorBrush(fgColor), 1);
                dc.DrawLine(pen, new Point(x, y + _cellHeight - 2), new Point(x + _cellWidth, y + _cellHeight - 2));
            }
            if (cell.Attributes.Strikethrough)
            {
                var pen = new Pen(new SolidColorBrush(fgColor), 1);
                dc.DrawLine(pen, new Point(x, y + _cellHeight * 0.5), new Point(x + _cellWidth, y + _cellHeight * 0.5));
            }
        }
        FlushRun();
    }

    private void DrawGlyph(DrawingContext dc, Rune r, Brush brush, double x, double baseline, bool bold)
    {
        var gtf = (bold && _boldGlyphTypeface != null) ? _boldGlyphTypeface! : _glyphTypeface;
        if (gtf == null) return;
        string s = r.ToString();
        if (s.Length == 0) return;
        if (!gtf.CharacterToGlyphMap.TryGetValue(s[0], out ushort glyphIndex)) return;
        try
        {
            var glyphRun = new GlyphRun(
                gtf, 0, false, FontSize,
                (float)VisualTreeHelper.GetDpi(this).PixelsPerDip,
                new[] { glyphIndex },
                new Point(x, baseline),
                new[] { _cellWidth },
                null, null, null, null, null, null);
            dc.DrawGlyphRun(brush, glyphRun);
        }
        catch { }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MeasureCells();
        if (_cellWidth > 0 && _cellHeight > 0)
        {
            int newCols = Math.Max(1, (int)(ActualWidth / _cellWidth));
            int newRows = Math.Max(1, (int)(ActualHeight / _cellHeight));
            if (newCols != TerminalColumns || newRows != TerminalRows)
            {
                TerminalColumns = newCols;
                TerminalRows = newRows;
                _emulator?.Resize(newCols, newRows);
                _scrollOffset = 0; // reflow changes scrollback layout; reset to live view
                ScrollbackChanged?.Invoke(this, EventArgs.Empty);
                QueueRender();

                // Debounce SSH SIGWINCH so rapid drag-resize doesn't flood the server
                _pendingResize = (newCols, newRows);
                if (_resizeDebounce == null)
                {
                    _resizeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                    _resizeDebounce.Tick += (_, _) =>
                    {
                        _resizeDebounce.Stop();
                        _session?.Resize(_pendingResize.cols, _pendingResize.rows);
                    };
                }
                _resizeDebounce.Stop();
                _resizeDebounce.Start();
            }
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key effectiveKey = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool alt  = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        // Password / input prompt mode: capture input without (or with) echo
        if (_isPasswordMode || _isInputMode)
        {
            if (effectiveKey == Key.Return)
            {
                string pwd = _passwordBuffer.ToString();
                _passwordBuffer.Clear();
                _isPasswordMode = false;
                _isInputMode    = false;
                var tcs = _passwordTcs;
                _passwordTcs = null;
                _emulator?.ProcessBytes("\r\n"u8.ToArray(), 0, 2);
                QueueRender();
                tcs?.SetResult(pwd);
                e.Handled = true;
            }
            else if (effectiveKey == Key.Back)
            {
                if (_passwordBuffer.Length > 0)
                {
                    _passwordBuffer.Remove(_passwordBuffer.Length - 1, 1);
                    if (_isInputMode)
                    {
                        // Erase the last echoed character: backspace + space + backspace
                        _emulator?.ProcessBytes("\x08 \x08"u8.ToArray(), 0, 3);
                        QueueRender();
                    }
                }
                e.Handled = true;
            }
            // Do NOT mark other keys as handled — TextInput must fire so
            // OnTextInput can append characters to the buffer.
            return;
        }

        // WPF routes some keys as e.Key == Key.System (e.g., F10, Alt+F4)
        // Ctrl+Shift+V = paste
        if (ctrl && shift && effectiveKey == Key.V)
        {
            if (_session?.IsConnected == true && Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (_emulator?.BracketedPaste == true)
                    _session.Send("\x1b[200~" + text + "\x1b[201~");
                else
                    _session.Send(text);
                e.Handled = true;
                return;
            }
        }

        // Ctrl+Shift+D = diagnostic buffer dump
        if (ctrl && shift && effectiveKey == Key.D)
        {
            DumpBufferToFile();
            e.Handled = true;
            return;
        }

        if (_session == null || !_session.IsConnected) return;

        // PowerEdit: track command buffer and intercept "poweredit <file>" on Enter
        if (EnablePowerEdit && !ctrl && !alt)
        {
            if (effectiveKey == Key.Back && !shift)
            {
                if (_commandBuffer.Length > 0)
                    _commandBuffer.Remove(_commandBuffer.Length - 1, 1);
            }
            else if (effectiveKey == Key.Up || effectiveKey == Key.Down)
            {
                _commandBuffer.Clear(); // history navigation — buffer is stale
            }
            else if (effectiveKey == Key.Return && !shift)
            {
                _commandBuffer.Clear();
                // Read the command directly from the terminal buffer — this captures
                // tab-completed text that was echoed back by the shell and never
                // passed through OnTextInput.
                string cmd = GetCurrentLineCommand();
                if (cmd.Equals("poweredit", StringComparison.OrdinalIgnoreCase) ||
                    cmd.StartsWith("poweredit ", StringComparison.OrdinalIgnoreCase))
                {
                    _session.Send("\r"); // let bash echo + run the silent ~/bin/poweredit script
                    PowerEditCommand?.Invoke(this, cmd);
                    e.Handled = true;
                    return;
                }
                // Normal Enter: let MapKey send \r below
            }
        }
        else if (EnablePowerEdit && ctrl && !alt)
        {
            // Ctrl+U (erase line) or Ctrl+C (cancel) invalidate the buffer
            if (effectiveKey == Key.U || effectiveKey == Key.C)
                _commandBuffer.Clear();
        }

        bool appCursor = _emulator?.ApplicationCursorKeys == true;
        byte[]? seq = MapKey(effectiveKey, ctrl, alt, shift, appCursor);
        if (seq != null)
        {
            _session.Send(seq);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_isPasswordMode || _isInputMode)
        {
            foreach (char c in e.Text)
            {
                if (c >= 0x20 && c != 0x7F)
                {
                    _passwordBuffer.Append(c);
                    if (_isInputMode)
                    {
                        // Echo the character visibly
                        var bytes = System.Text.Encoding.UTF8.GetBytes(c.ToString());
                        _emulator?.ProcessBytes(bytes, 0, bytes.Length);
                        QueueRender();
                    }
                }
            }
            e.Handled = true;
            return;
        }
        if (_session == null || !_session.IsConnected || string.IsNullOrEmpty(e.Text)) return;
        foreach (char c in e.Text)
        {
            if (c < 0x20 || c == 0x7F) return;
        }
        if (EnablePowerEdit)
            _commandBuffer.Append(e.Text);
        if (_emulator?.BracketedPaste == true && e.Text.Length > 1)
            _session.Send("\x1b[200~" + e.Text + "\x1b[201~");
        else
            _session.Send(e.Text);
        e.Handled = true;
    }

    /// <summary>
    /// Reads the current cursor row from the terminal buffer and returns the portion
    /// starting from "poweredit" (case-insensitive). Returns empty string if not found.
    /// This is more reliable than tracking keystrokes because it captures text echoed
    /// back by the shell (e.g. tab-completed filenames).
    /// </summary>
    private string GetCurrentLineCommand()
    {
        if (_emulator == null) return string.Empty;
        var buf = _emulator.Buffer;
        int row = buf.CursorRow;

        var sb = new StringBuilder(buf.Columns);
        for (int c = 0; c < buf.Columns; c++)
            sb.Append(buf.GetCellCopy(row, c).Character.ToString());

        string line = sb.ToString().TrimEnd();

        // Extract from "poweredit" onwards, ignoring the shell prompt prefix
        int idx = line.IndexOf("poweredit", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? line[idx..] : string.Empty;
    }

    /// <summary>
    /// Parses the shell prompt on the current cursor row to extract the current working
    /// directory. Handles the common bash PS1 pattern: user@host:PATH$ or user@host:PATH#
    /// Returns empty string if the CWD cannot be determined.
    /// </summary>
    public string GetCurrentWorkingDir()
    {
        if (_emulator == null) return string.Empty;
        var buf = _emulator.Buffer;
        int row = buf.CursorRow;

        var sb = new StringBuilder(buf.Columns);
        for (int c = 0; c < buf.Columns; c++)
            sb.Append(buf.GetCellCopy(row, c).Character.ToString());

        string line = sb.ToString().TrimEnd();

        // Find where the command starts so we only look at the prompt portion
        int cmdIdx = line.IndexOf("poweredit", StringComparison.OrdinalIgnoreCase);
        string prompt = cmdIdx > 0 ? line[..cmdIdx] : line;

        // Common bash prompt pattern: ends with ":PATH$ " or ":PATH# "
        // Find the last colon and extract up to the prompt delimiter ($ or #)
        int colonIdx = prompt.LastIndexOf(':');
        if (colonIdx < 0) return string.Empty;

        string afterColon = prompt[(colonIdx + 1)..];
        // Trim the prompt delimiter and trailing spaces
        int delimIdx = -1;
        for (int i = afterColon.Length - 1; i >= 0; i--)
        {
            char ch = afterColon[i];
            if (ch == '$' || ch == '#' || ch == '%')
            {
                delimIdx = i;
                break;
            }
        }
        string dir = delimIdx >= 0 ? afterColon[..delimIdx].Trim() : afterColon.Trim();

        return (dir.StartsWith("/") || dir.StartsWith("~")) ? dir : string.Empty;
    }

    private static byte[]? MapKey(Key key, bool ctrl, bool alt, bool shift, bool appCursor)
    {
        string prefix = alt ? "\x1b" : "";

        if (ctrl && !alt)
        {
            return key switch
            {
                Key.A => new[] { (byte)0x01 }, Key.B => new[] { (byte)0x02 },
                Key.C => new[] { (byte)0x03 }, Key.D => new[] { (byte)0x04 },
                Key.E => new[] { (byte)0x05 }, Key.F => new[] { (byte)0x06 },
                Key.G => new[] { (byte)0x07 }, Key.H => new[] { (byte)0x08 },
                Key.I => new[] { (byte)0x09 }, Key.J => new[] { (byte)0x0A },
                Key.K => new[] { (byte)0x0B }, Key.L => new[] { (byte)0x0C },
                Key.M => new[] { (byte)0x0D }, Key.N => new[] { (byte)0x0E },
                Key.O => new[] { (byte)0x0F }, Key.P => new[] { (byte)0x10 },
                Key.Q => new[] { (byte)0x11 }, Key.R => new[] { (byte)0x12 },
                Key.S => new[] { (byte)0x13 }, Key.T => new[] { (byte)0x14 },
                Key.U => new[] { (byte)0x15 }, Key.V => new[] { (byte)0x16 },
                Key.W => new[] { (byte)0x17 }, Key.X => new[] { (byte)0x18 },
                Key.Y => new[] { (byte)0x19 }, Key.Z => new[] { (byte)0x1A },
                Key.OemOpenBrackets => new[] { (byte)0x1B },
                Key.OemBackslash => new[] { (byte)0x1C },
                Key.OemCloseBrackets => new[] { (byte)0x1D },
                Key.D6 => new[] { (byte)0x1E },
                Key.OemMinus => new[] { (byte)0x1F },
                Key.Back => new[] { (byte)0x08 },
                _ => null
            };
        }

        string up    = appCursor ? "\x1bOA" : "\x1b[A";
        string down  = appCursor ? "\x1bOB" : "\x1b[B";
        string right = appCursor ? "\x1bOC" : "\x1b[C";
        string left  = appCursor ? "\x1bOD" : "\x1b[D";

        string? s = key switch
        {
            Key.Up      => prefix + up,
            Key.Down    => prefix + down,
            Key.Right   => prefix + right,
            Key.Left    => prefix + left,
            Key.Home    => prefix + "\x1b[H",
            Key.End     => prefix + "\x1b[F",
            Key.Insert  => prefix + "\x1b[2~",
            Key.Delete  => prefix + "\x1b[3~",
            Key.Prior   => prefix + "\x1b[5~",
            Key.Next    => prefix + "\x1b[6~",
            Key.F1      => prefix + "\x1bOP",
            Key.F2      => prefix + "\x1bOQ",
            Key.F3      => prefix + "\x1bOR",
            Key.F4      => prefix + "\x1bOS",
            Key.F5      => prefix + "\x1b[15~",
            Key.F6      => prefix + "\x1b[17~",
            Key.F7      => prefix + "\x1b[18~",
            Key.F8      => prefix + "\x1b[19~",
            Key.F9      => prefix + "\x1b[20~",
            Key.F10     => prefix + "\x1b[21~",
            Key.F11     => prefix + "\x1b[23~",
            Key.F12     => prefix + "\x1b[24~",
            Key.Back    => prefix + "\x7f",
            Key.Tab     => shift ? prefix + "\x1b[Z" : prefix + "\x09",
            Key.Return  => prefix + "\r",
            Key.Escape  => "\x1b",
            _ => null
        };

        return s != null ? System.Text.Encoding.UTF8.GetBytes(s) : null;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton == MouseButton.Left && _emulator?.MouseMode == MouseMode.None)
        {
            _isSelecting = true;
            _selStart = PointToCell(e.GetPosition(this));
            _selEnd = _selStart;
            CaptureMouse();
            QueueRender();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Right && CopyPasteMode == TerminalCopyPasteMode.Classic
            && _emulator?.MouseMode == MouseMode.None)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }
        if (_emulator?.MouseMode != MouseMode.None)
        {
            SendMouseEvent(e.GetPosition(this), e.ChangedButton, true);
            e.Handled = true;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_isSelecting && e.ChangedButton == MouseButton.Left)
        {
            _isSelecting = false;
            _selEnd = PointToCell(e.GetPosition(this));
            ReleaseMouseCapture();
            if (_selStart == _selEnd)
            {
                _selStart = null;
                _selEnd = null;
            }
            else if (CopyPasteMode == TerminalCopyPasteMode.Classic)
            {
                CopySelection();
            }
            QueueRender();
            e.Handled = true;
            return;
        }
        if (_emulator?.MouseMode != MouseMode.None)
        {
            SendMouseEvent(e.GetPosition(this), e.ChangedButton, false);
            e.Handled = true;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isSelecting && e.LeftButton == MouseButtonState.Pressed)
        {
            _selEnd = PointToCell(e.GetPosition(this));
            QueueRender();
            return;
        }
        if (_emulator?.MouseMode == MouseMode.AnyEvent ||
            (_emulator?.MouseMode == MouseMode.ButtonEvent && e.LeftButton == MouseButtonState.Pressed))
        {
            SendMouseMotionEvent(e.GetPosition(this), e);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_session?.IsConnected != true) return;
        if (_emulator?.MouseMode != MouseMode.None)
        {
            var pos = e.GetPosition(this);
            int col = (int)(pos.X / _cellWidth) + 1;
            int row = (int)(pos.Y / _cellHeight) + 1;
            int btn = e.Delta > 0 ? 64 : 65;
            if (_emulator?.MouseSgrMode == true)
                _session.Send($"\x1b[<{btn};{col};{row}M");
            else
                _session.Send(new byte[] { 0x1b, (byte)'[', (byte)'M', (byte)(btn + 32), (byte)(col + 32), (byte)(row + 32) });
        }
        else if (_scrollOffset > 0 || e.Delta > 0)
        {
            int lines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
            _scrollOffset += e.Delta > 0 ? lines : -lines; // up=positive=scroll into history
            _scrollOffset = Math.Clamp(_scrollOffset, 0, _emulator?.Buffer.ScrollbackCount ?? 0);
            ScrollbackChanged?.Invoke(this, EventArgs.Empty);
            QueueRender();
            e.Handled = true;
            return;
        }
        else
        {
            string seq = e.Delta > 0 ? "\x1b[5~" : "\x1b[6~";
            _session?.Send(seq);
        }
        e.Handled = true;
        base.OnMouseWheel(e);
    }

    private void SendMouseEvent(Point pos, MouseButton btn, bool pressed)
    {
        if (_emulator?.MouseMode == MouseMode.None || _session?.IsConnected != true) return;
        int col = Math.Max(1, Math.Min((int)(pos.X / _cellWidth) + 1, TerminalColumns));
        int row = Math.Max(1, Math.Min((int)(pos.Y / _cellHeight) + 1, TerminalRows));
        int b = btn switch { MouseButton.Left => 0, MouseButton.Middle => 1, MouseButton.Right => 2, _ => 3 };

        if (_emulator!.MouseSgrMode)
        {
            // SGR (1006): button number is preserved on release; M=press, m=release
            string m = pressed ? "M" : "m";
            _session.Send($"\x1b[<{b};{col};{row}{m}");
        }
        else
        {
            // Legacy X10: all releases encode as button 3
            if (!pressed) b = 3;
            if (col <= 222 && row <= 222)
                _session.Send(new byte[] { 0x1b, (byte)'[', (byte)'M', (byte)(b + 32), (byte)(col + 32), (byte)(row + 32) });
        }
    }

    private void SendMouseMotionEvent(Point pos, MouseEventArgs e)
    {
        if (_emulator?.MouseMode == MouseMode.None || _session?.IsConnected != true) return;
        int col = Math.Max(1, Math.Min((int)(pos.X / _cellWidth) + 1, TerminalColumns));
        int row = Math.Max(1, Math.Min((int)(pos.Y / _cellHeight) + 1, TerminalRows));
        int b = 32;
        if (e is MouseButtonEventArgs mb)
            b += mb.ChangedButton switch { MouseButton.Left => 0, MouseButton.Middle => 1, MouseButton.Right => 2, _ => 0 };

        if (_emulator!.MouseSgrMode)
            _session.Send($"\x1b[<{b};{col};{row}M");
        else if (col <= 222 && row <= 222)
            _session.Send(new byte[] { 0x1b, (byte)'[', (byte)'M', (byte)(b + 32), (byte)(col + 32), (byte)(row + 32) });
    }

    private (int row, int col) PointToCell(Point pos)
    {
        int col = Math.Clamp((int)(pos.X / _cellWidth), 0, TerminalColumns - 1);
        int row = Math.Clamp((int)(pos.Y / _cellHeight), 0, TerminalRows - 1);
        return (row, col);
    }

    private bool IsInSelection(int row, int col)
    {
        if (_selStart == null || _selEnd == null) return false;
        var (r1, c1) = _selStart.Value;
        var (r2, c2) = _selEnd.Value;
        if (r1 > r2 || (r1 == r2 && c1 > c2)) { (r1, c1, r2, c2) = (r2, c2, r1, c1); }
        if (row < r1 || row > r2) return false;
        if (row == r1 && col < c1) return false;
        if (row == r2 && col > c2) return false;
        return true;
    }

    private string GetSelectedText()
    {
        if (_selStart == null || _selEnd == null) return "";
        var buf = _emulator?.Buffer;
        if (buf == null) return "";
        var (r1, c1) = _selStart.Value;
        var (r2, c2) = _selEnd.Value;
        if (r1 > r2 || (r1 == r2 && c1 > c2)) { (r1, c1, r2, c2) = (r2, c2, r1, c1); }

        var sb = new StringBuilder();
        for (int row = r1; row <= r2; row++)
        {
            int startCol = (row == r1) ? c1 : 0;
            int endCol   = (row == r2) ? c2 : buf.Columns - 1;
            for (int col = startCol; col <= endCol; col++)
            {
                var cell = buf.GetCellCopy(row, col);
                if (!cell.IsWideRight)
                    sb.Append(cell.Character.Value <= 0x20 ? ' ' : cell.Character.ToString());
            }
            if (row < r2) sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private void CopySelection()
    {
        string text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void PasteFromClipboard()
    {
        if (_session?.IsConnected == true && Clipboard.ContainsText())
        {
            string text = Clipboard.GetText();
            if (_emulator?.BracketedPaste == true)
                _session.Send("\x1b[200~" + text + "\x1b[201~");
            else
                _session.Send(text);
        }
    }

    public event EventHandler? ScrollbackChanged;

    private void DumpBufferToFile()
    {
        if (_emulator == null) return;
        var buf = _emulator.Buffer;
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "terminal_dump.txt");
        var defaultBg = TerminalColor.DefaultBg.Resolve(false, false);

        using var sw = new System.IO.StreamWriter(path, false, System.Text.Encoding.UTF8);
        sw.WriteLine($"=== Buffer dump {DateTime.Now:HH:mm:ss.fff} ===");
        sw.WriteLine($"Cols={buf.Columns} Rows={buf.Rows} Scrollback={buf.ScrollbackCount}");
        sw.WriteLine($"Cursor=({buf.CursorRow},{buf.CursorCol}) ScrollTop={buf.ScrollTop} ScrollBottom={buf.ScrollBottom}");
        sw.WriteLine($"CurrentBg=#{buf.CurrentAttributes.Background.Resolve(false,false).R:X2}{buf.CurrentAttributes.Background.Resolve(false,false).G:X2}{buf.CurrentAttributes.Background.Resolve(false,false).B:X2}");
        sw.WriteLine($"ScrollOffset={_scrollOffset}");
        sw.WriteLine();

        sw.WriteLine("--- Scrollback (oldest first) ---");
        for (int r = 0; r < buf.ScrollbackCount; r++)
        {
            var cells = buf.GetScrollbackRow(r);
            bool softWrap = buf.GetScrollbackRowSoftWrap(r);
            var rowBgs = GetRowBgSummary(cells, buf.Columns, defaultBg);
            string text = new string(cells.Take(buf.Columns).Select(c => c.Character.Value < 32 ? '.' : (char)c.Character.Value).ToArray());
            sw.WriteLine($"SB[{r:3}] sw={softWrap} bg=[{rowBgs}] '{text.TrimEnd()}'");
        }

        sw.WriteLine();
        sw.WriteLine("--- Main buffer ---");
        for (int r = 0; r < buf.Rows; r++)
        {
            bool softWrap = buf.GetRowSoftWrap(r);
            bool isCursor = (r == buf.CursorRow);
            var rowCells = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(r, c)).ToArray();
            var rowBgs = GetRowBgSummary(rowCells, buf.Columns, defaultBg);
            string text = new string(rowCells.Select(c => c.Character.Value < 32 ? '.' : (char)c.Character.Value).ToArray());
            sw.WriteLine($"MB[{r:2}]{(isCursor ? "*" : " ")} sw={softWrap} bg=[{rowBgs}] '{text.TrimEnd()}'");
        }
        sw.WriteLine($"=== End dump ===");

        System.Windows.MessageBox.Show($"Buffer dumped to:\n{path}", "Dump complete");
    }

    private string GetRowBgSummary(IEnumerable<ScreenCell> cells, int cols, System.Windows.Media.Color defaultBg)
    {
        // Build a compact representation: groups of non-default background cells shown as hex ranges
        var list = cells.Take(cols).ToList();
        var sb = new System.Text.StringBuilder();
        for (int c = 0; c < list.Count; c++)
        {
            var (_, bg) = list[c].Attributes.EffectiveColors;
            var resolved = bg.Resolve(false, false);
            if (resolved != defaultBg)
            {
                int start = c;
                while (c < list.Count - 1)
                {
                    var (_, nbg) = list[c + 1].Attributes.EffectiveColors;
                    if (nbg.Resolve(false, false) != resolved) break;
                    c++;
                }
                if (sb.Length > 0) sb.Append(',');
                sb.Append($"col{start}-{c}=#{resolved.R:X2}{resolved.G:X2}{resolved.B:X2}");
            }
        }
        return sb.Length == 0 ? "default" : sb.ToString();
    }

    /// <summary>Height in pixels of one terminal cell row. Used for snap-to-grid window resizing.</summary>
    public double CharHeight => _cellHeight;

    public int ScrollbackCount => _emulator?.Buffer.ScrollbackCount ?? 0;

    public int ScrollOffset
    {
        get => _scrollOffset;
        set
        {
            int max = ScrollbackCount;
            _scrollOffset = Math.Clamp(value, 0, max);
            QueueRender();
        }
    }

    public void EnsureEmulatorInitialized()
    {
        if (_emulator != null) return;
        _emulator = new TerminalEmulator(TerminalColumns > 0 ? TerminalColumns : 80, TerminalRows > 0 ? TerminalRows : 24);
        _emulator.TitleChanged += (_, _) => { };
        _emulator.BellRaised += (_, _) => System.Media.SystemSounds.Beep.Play();
        _emulator.CursorVisibilityChanged += (_, _) => QueueRender();
        _emulator.ResponseReady += (_, _) => { };
        _emulator.Buffer.MarkAllDirty();
        QueueRender();
    }

    /// <summary>
    /// Writes a status or error message directly into the terminal display.
    /// Safe to call from any thread.
    /// </summary>
    public void WriteStatusMessage(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Dispatcher.BeginInvoke(() =>
        {
            EnsureEmulatorInitialized();
            _emulator!.ProcessBytes(bytes, 0, bytes.Length);
            _scrollOffset = 0;
            QueueRender();
        }, DispatcherPriority.Normal);
    }

    public async Task<string> PromptForPasswordAsync(string promptText, CancellationToken ct)
    {
        TaskCompletionSource<string> tcs = null!;
        await Dispatcher.InvokeAsync(() =>
        {
            EnsureEmulatorInitialized();
            var bytes = System.Text.Encoding.UTF8.GetBytes(promptText);
            _emulator!.ProcessBytes(bytes, 0, bytes.Length);
            _passwordBuffer.Clear();
            _isPasswordMode = true;
            tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _passwordTcs = tcs;
            QueueRender();
        });
        return await tcs.Task;
    }

    /// <summary>
    /// Shows <paramref name="promptText"/> in the terminal and reads a line of
    /// visible input (characters are echoed). Used for non-secret prompts.
    /// </summary>
    public async Task<string> PromptForInputAsync(string promptText, CancellationToken ct)
    {
        TaskCompletionSource<string> tcs = null!;
        await Dispatcher.InvokeAsync(() =>
        {
            EnsureEmulatorInitialized();
            var bytes = System.Text.Encoding.UTF8.GetBytes(promptText);
            _emulator!.ProcessBytes(bytes, 0, bytes.Length);
            _passwordBuffer.Clear();
            _isInputMode = true;
            tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _passwordTcs = tcs;
            QueueRender();
        });
        return await tcs.Task;
    }
}
