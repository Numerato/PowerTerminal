namespace Terminal.Vt;

using System.Text;

public enum MouseMode{ None, X10, ButtonEvent, AnyEvent }
public enum CursorStyle { BlinkingBlock, SteadyBlock, BlinkingUnderline, SteadyUnderline, BlinkingBar, SteadyBar }

file sealed class Charset
{
    public const int Ascii = 0;
    public const int DecSpecialGraphics = 1;

    // PuTTY-sourced DEC VT100 special graphics map (0x60-0x7E → Unicode)
    private static readonly char[] DecMap =
    {
        '◆','▒','␉','␌','␍','␊','°','±','␤','␋',
        '┘','┐','┌','└','┼','⎺','⎻','─','⎼','⎽',
        '├','┤','┴','┬','│','≤','≥','π','≠','£','·'
    };

    public static Rune Translate(Rune r)
    {
        int c = r.Value;
        if (c >= 0x60 && c <= 0x7E)
            return new Rune(DecMap[c - 0x60]);
        return r;
    }
}

public sealed class TerminalEmulator : IVtParserActions
{
    private readonly VtParser _parser;
    public ScreenBuffer Buffer { get; }

    public bool ApplicationCursorKeys { get; private set; }
    public bool ApplicationKeypad { get; private set; }
    public bool BracketedPaste { get; private set; }
    public MouseMode MouseMode { get; private set; }
    public bool MouseSgrMode { get; private set; }
    public CursorStyle CursorStyle { get; private set; } = CursorStyle.BlinkingBlock;

    // DEC character set state (G0/G1 + shift-in/out)
    private int _charsetG0 = Charset.Ascii;
    private int _charsetG1 = Charset.Ascii;
    private bool _useG1;   // SO shifts to G1; SI shifts back to G0

    public event EventHandler<string>? TitleChanged;
    public event EventHandler? BellRaised;
    public event EventHandler? CursorVisibilityChanged;

    // For responding to DSR requests
    public event EventHandler<string>? ResponseReady;

    public TerminalEmulator(int columns, int rows)
    {
        Buffer = new ScreenBuffer(columns, rows);
        _parser = new VtParser(this);
    }

    public void ProcessBytes(byte[] data, int offset, int length) => _parser.Feed(data, offset, length);
    public void ProcessBytes(ReadOnlySpan<byte> data) => _parser.Feed(data);

    public void Resize(int columns, int rows) => Buffer.Resize(columns, rows);

    public void Reset()
    {
        Buffer.Reset();
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPaste = false;
        MouseMode = MouseMode.None;
        MouseSgrMode = false;
        CursorStyle = CursorStyle.BlinkingBlock;
        _charsetG0 = Charset.Ascii;
        _charsetG1 = Charset.Ascii;
        _useG1 = false;
    }

    void IVtParserActions.Print(Rune r)
    {
        // Apply active character set translation (DEC Special Graphics etc.)
        if ((_useG1 ? _charsetG1 : _charsetG0) == Charset.DecSpecialGraphics)
            r = Charset.Translate(r);
        Buffer.WriteRune(r);
    }

    void IVtParserActions.Execute(byte c)
    {
        switch (c)
        {
            case 0x07: BellRaised?.Invoke(this, EventArgs.Empty); break;
            case 0x08: Buffer.Backspace(); break;
            case 0x09: Buffer.Tab(); break;
            case 0x0A: // LF
            case 0x0B: // VT
            case 0x0C: // FF
                Buffer.LineFeed();
                break;
            case 0x0D: Buffer.CarriageReturn(); break;
            case 0x0E: _useG1 = true; break;   // SO — shift out to G1
            case 0x0F: _useG1 = false; break;  // SI — shift in to G0
        }
    }

    void IVtParserActions.CsiDispatch(byte finalByte, IReadOnlyList<int> parameters, IReadOnlyList<byte> intermediates, bool isPrivate)
    {
        if (isPrivate)
        {
            HandleDecPrivateMode(finalByte, parameters);
            return;
        }

        int p0 = GetParam(parameters, 0, -1);
        int p1 = GetParam(parameters, 1, -1);

        switch ((char)finalByte)
        {
            case 'A': Buffer.MoveCursorRow(-(p0 < 1 ? 1 : p0)); break;
            case 'B': Buffer.MoveCursorRow(p0 < 1 ? 1 : p0); break;
            case 'C': Buffer.MoveCursorCol(p0 < 1 ? 1 : p0); break;
            case 'D': Buffer.MoveCursorCol(-(p0 < 1 ? 1 : p0)); break;
            case 'E':
                Buffer.MoveCursorRow(p0 < 1 ? 1 : p0);
                Buffer.CarriageReturn();
                break;
            case 'F':
                Buffer.MoveCursorRow(-(p0 < 1 ? 1 : p0));
                Buffer.CarriageReturn();
                break;
            case 'G':
                Buffer.SetCursorCol((p0 < 1 ? 1 : p0) - 1);
                break;
            case 'H':
            case 'f':
                Buffer.SetCursorPosition(
                    (p0 < 1 ? 1 : p0) - 1,
                    (p1 < 1 ? 1 : p1) - 1);
                break;
            case 'I': Buffer.Tab(p0 < 1 ? 1 : p0); break;
            case 'J': Buffer.EraseDisplay(p0 < 0 ? 0 : p0); break;
            case 'K': Buffer.EraseLine(p0 < 0 ? 0 : p0); break;
            case 'L': Buffer.InsertLines(p0 < 1 ? 1 : p0); break;
            case 'M': Buffer.DeleteLines(p0 < 1 ? 1 : p0); break;
            case 'P': Buffer.DeleteChars(p0 < 1 ? 1 : p0); break;
            case 'S': Buffer.ScrollUp(p0 < 1 ? 1 : p0); break;
            case 'T': Buffer.ScrollDown(p0 < 1 ? 1 : p0); break;
            case 'X': Buffer.EraseChars(p0 < 1 ? 1 : p0); break;
            case 'Z':
                // Cursor back tab
                int tabCount = p0 < 1 ? 1 : p0;
                for (int i = 0; i < tabCount; i++)
                {
                    int col = Buffer.CursorCol;
                    if (col == 0) break;
                    int newCol = ((col - 1) / 8) * 8;
                    Buffer.SetCursorCol(newCol);
                }
                break;
            case '@': Buffer.InsertChars(p0 < 1 ? 1 : p0); break;
            case 'd': Buffer.SetCursorRow((p0 < 1 ? 1 : p0) - 1); break;
            case 'm': HandleSgr(parameters); break;
            case 'n':
                if (p0 == 6)
                    ResponseReady?.Invoke(this, $"\x1b[{Buffer.CursorRow + 1};{Buffer.CursorCol + 1}R");
                break;
            case 'r':
                Buffer.SetScrollRegion(p0 < 1 ? 1 : p0, p1 < 1 ? Buffer.Rows : p1);
                break;
            case 's': Buffer.SaveCursor(); break;
            case 'u': Buffer.RestoreCursor(); break;
            case 'q':
                // DECSCUSR — cursor style (requires intermediate SP 0x20)
                if (intermediates.Count == 1 && intermediates[0] == 0x20)
                    CursorStyle = (p0 < 0 ? 0 : p0) switch {
                        0 or 1 => CursorStyle.BlinkingBlock,
                        2 => CursorStyle.SteadyBlock,
                        3 => CursorStyle.BlinkingUnderline,
                        4 => CursorStyle.SteadyUnderline,
                        5 => CursorStyle.BlinkingBar,
                        6 => CursorStyle.SteadyBar,
                        _ => CursorStyle.BlinkingBlock
                    };
                break;
            case 'h': // Set mode (normal)
                foreach (var p in parameters)
                    HandleMode(p, true);
                break;
            case 'l': // Reset mode (normal)
                foreach (var p in parameters)
                    HandleMode(p, false);
                break;
        }
    }

    private void HandleDecPrivateMode(byte finalByte, IReadOnlyList<int> parameters)
    {
        bool set = (char)finalByte == 'h';
        if ((char)finalByte != 'h' && (char)finalByte != 'l') return;

        foreach (var param in parameters.Count == 0 ? new List<int> { -1 } : parameters)
        {
            switch (param)
            {
                case 1: ApplicationCursorKeys = set; break;
                case 5: // reverse video screen - could implement but complex; ignore for now
                    break;
                case 6: Buffer.OriginMode = set; break;
                case 7: Buffer.AutoWrap = set; break;
                case 12: // cursor blink - ignore
                    break;
                case 25:
                    Buffer.CursorVisible = set;
                    CursorVisibilityChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case 1000: MouseMode = set ? MouseMode.X10 : MouseMode.None; break;
                case 1002: MouseMode = set ? MouseMode.ButtonEvent : MouseMode.None; break;
                case 1003: MouseMode = set ? MouseMode.AnyEvent : MouseMode.None; break;
                case 1006: MouseSgrMode = set; break;
                case 1049:
                    if (set)
                    {
                        Buffer.SaveCursor();
                        Buffer.SwitchToAltBuffer();
                        Buffer.EraseDisplay(2);
                    }
                    else
                    {
                        Buffer.SwitchToMainBuffer();
                        Buffer.RestoreCursor();
                    }
                    break;
                case 2004: BracketedPaste = set; break;
            }
        }
    }

    private void HandleMode(int param, bool set)
    {
        // Standard ANSI modes (not private)
        switch (param)
        {
            case 4: // Insert mode - ignore for now
                break;
        }
    }

    private void HandleSgr(IReadOnlyList<int> parameters)
    {
        var attrs = Buffer.CurrentAttributes;
        if (parameters.Count == 0) { attrs = CharacterAttributes.Default; Buffer.CurrentAttributes = attrs; return; }

        int i = 0;
        while (i < parameters.Count)
        {
            int p = parameters[i] < 0 ? 0 : parameters[i];
            switch (p)
            {
                case 0: attrs = CharacterAttributes.Default; break;
                case 1: attrs.Bold = true; break;
                case 2: attrs.Dim = true; break;
                case 3: attrs.Italic = true; break;
                case 4: attrs.Underline = true; break;
                case 5: attrs.Blink = true; break;
                case 7: attrs.ReverseVideo = true; break;
                case 8: attrs.Invisible = true; break;
                case 9: attrs.Strikethrough = true; break;
                case 21: attrs.Underline = true; break;
                case 22: attrs.Bold = false; attrs.Dim = false; break;
                case 23: attrs.Italic = false; break;
                case 24: attrs.Underline = false; break;
                case 25: attrs.Blink = false; break;
                case 27: attrs.ReverseVideo = false; break;
                case 28: attrs.Invisible = false; break;
                case 29: attrs.Strikethrough = false; break;
                case >= 30 and <= 37: attrs.Foreground = TerminalColor.FromIndex(p - 30); break;
                case 38:
                    if (i + 1 < parameters.Count)
                    {
                        int sub = parameters[i + 1];
                        if (sub == 5 && i + 2 < parameters.Count) { attrs.Foreground = TerminalColor.FromIndex(parameters[i + 2]); i += 2; }
                        else if (sub == 2 && i + 4 < parameters.Count) { attrs.Foreground = TerminalColor.FromRgb((byte)parameters[i + 2], (byte)parameters[i + 3], (byte)parameters[i + 4]); i += 4; }
                    }
                    break;
                case 39: attrs.Foreground = TerminalColor.DefaultFg; break;
                case >= 40 and <= 47: attrs.Background = TerminalColor.FromIndex(p - 40); break;
                case 48:
                    if (i + 1 < parameters.Count)
                    {
                        int sub = parameters[i + 1];
                        if (sub == 5 && i + 2 < parameters.Count) { attrs.Background = TerminalColor.FromIndex(parameters[i + 2]); i += 2; }
                        else if (sub == 2 && i + 4 < parameters.Count) { attrs.Background = TerminalColor.FromRgb((byte)parameters[i + 2], (byte)parameters[i + 3], (byte)parameters[i + 4]); i += 4; }
                    }
                    break;
                case 49: attrs.Background = TerminalColor.DefaultBg; break;
                case >= 90 and <= 97: attrs.Foreground = TerminalColor.FromIndex(p - 90 + 8); break;
                case >= 100 and <= 107: attrs.Background = TerminalColor.FromIndex(p - 100 + 8); break;
            }
            i++;
        }
        Buffer.CurrentAttributes = attrs;
    }

    void IVtParserActions.EscDispatch(byte finalByte, IReadOnlyList<byte> intermediates)
    {
        // ESC ( x  — designate G0 charset;  ESC ) x — designate G1 charset
        if (intermediates.Count == 1)
        {
            int target = intermediates[0] == (byte)'(' ? 0 : (intermediates[0] == (byte)')' ? 1 : -1);
            if (target == 0)
            {
                _charsetG0 = (char)finalByte == '0' ? Charset.DecSpecialGraphics : Charset.Ascii;
                return;
            }
            if (target == 1)
            {
                _charsetG1 = (char)finalByte == '0' ? Charset.DecSpecialGraphics : Charset.Ascii;
                return;
            }
        }

        switch ((char)finalByte)
        {
            case '7': Buffer.SaveCursor(); break;
            case '8': Buffer.RestoreCursor(); break;
            case 'M': Buffer.ReverseLineFeed(); break;
            case 'D': Buffer.LineFeed(); break;
            case 'E': Buffer.CarriageReturn(); Buffer.LineFeed(); break;
            case 'H': break; // Set tab stop - ignore
            case '=': ApplicationKeypad = true; break;
            case '>': ApplicationKeypad = false; break;
            case 'c': Reset(); break;
        }
    }

    void IVtParserActions.OscDispatch(string command)
    {
        // Format: <code>;<data>
        int semicolon = command.IndexOf(';');
        if (semicolon < 0) return;
        if (!int.TryParse(command.AsSpan(0, semicolon), out int code)) return;
        string data = command[(semicolon + 1)..];
        switch (code)
        {
            case 0:
            case 1:
            case 2:
                TitleChanged?.Invoke(this, data);
                break;
        }
    }

    void IVtParserActions.DcsHook(byte finalByte, IReadOnlyList<int> parameters, IReadOnlyList<byte> intermediates) { }
    void IVtParserActions.DcsPut(byte c) { }
    void IVtParserActions.DcsUnhook() { }

    private static int GetParam(IReadOnlyList<int> p, int index, int defaultVal) =>
        index < p.Count && p[index] >= 0 ? p[index] : defaultVal;
}
