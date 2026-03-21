namespace Terminal.Vt;

using System.Text;

public interface IVtParserActions
{
    void Print(Rune rune);
    void Execute(byte c);
    void CsiDispatch(byte finalByte, IReadOnlyList<int> parameters, IReadOnlyList<byte> intermediates, bool isPrivate);
    void EscDispatch(byte finalByte, IReadOnlyList<byte> intermediates);
    void OscDispatch(string command);
    void DcsHook(byte finalByte, IReadOnlyList<int> parameters, IReadOnlyList<byte> intermediates);
    void DcsPut(byte c);
    void DcsUnhook();
}

public sealed class VtParser
{
    private enum State
    {
        Ground, Escape, EscapeIntermediate,
        CsiEntry, CsiParam, CsiIntermediate, CsiIgnore,
        OscString, OscEscPending,
        DcsEntry, DcsParam, DcsIntermediate, DcsPassthrough, DcsIgnore,
        SosPmApcString
    }

    private State _state = State.Ground;
    private readonly List<byte> _intermediates = new();
    private readonly List<int> _params = new();
    private readonly System.Text.StringBuilder _oscBuffer = new();
    private bool _isPrivate;
    private int _currentParam = -1;

    // UTF-8 decoder state
    private int _utf8Remaining;
    private int _utf8Codepoint;

    private readonly IVtParserActions _actions;

    public VtParser(IVtParserActions actions) { _actions = actions; }

    public void Feed(byte[] data, int offset, int length)
    {
        for (int i = offset; i < offset + length; i++)
            ProcessByte(data[i]);
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            ProcessByte(b);
    }

    private void ProcessByte(byte b)
    {
        // Handle UTF-8 continuation bytes first (only in Ground state context)
        if (_utf8Remaining > 0)
        {
            if ((b & 0xC0) == 0x80)
            {
                _utf8Codepoint = (_utf8Codepoint << 6) | (b & 0x3F);
                _utf8Remaining--;
                if (_utf8Remaining == 0)
                {
                    try { _actions.Print(new Rune(_utf8Codepoint)); } catch { }
                }
                return;
            }
            else
            {
                // Invalid: reset UTF-8 and fall through
                _utf8Remaining = 0;
            }
        }

        // C0 controls: CAN (0x18) and SUB (0x1A) cancel current sequence
        if (b == 0x18 || b == 0x1A)
        {
            _actions.Execute(b);
            TransitionTo(State.Ground);
            return;
        }

        // ESC: in OscString, go to OscEscPending; otherwise start new escape
        if (b == 0x1B)
        {
            if (_state == State.OscString)
                TransitionTo(State.OscEscPending);
            else
                TransitionTo(State.Escape);
            return;
        }

        // BEL (0x07) in OscString terminates the OSC
        if (b == 0x07 && _state == State.OscString)
        {
            _actions.OscDispatch(_oscBuffer.ToString());
            TransitionTo(State.Ground);
            return;
        }

        // Other C0 controls (except ESC) execute in any state
        if (b < 0x20)
        {
            _actions.Execute(b);
            return;
        }

        // DEL (0x7F) is ignored in most states
        if (b == 0x7F)
            return;

        switch (_state)
        {
            case State.Ground:
                // 8-bit C1 controls (0x80-0x9F) — equivalent to ESC + 0x40-0x5F
                if (b >= 0x80 && b <= 0x9F)
                {
                    switch (b)
                    {
                        case 0x9B: TransitionTo(State.CsiEntry); break;   // CSI
                        case 0x9D: TransitionTo(State.OscString); break;  // OSC
                        case 0x9C: break;                                  // ST (standalone, ignore)
                        case 0x90: TransitionTo(State.DcsEntry); break;   // DCS
                        case 0x98: case 0x9E: case 0x9F:
                            TransitionTo(State.SosPmApcString); break;    // SOS/PM/APC
                        case 0x84: _actions.Execute(0x0A); break;         // IND → LF
                        case 0x85: _actions.Execute(0x0D); _actions.Execute(0x0A); break; // NEL → CR LF
                        case 0x8D:                                         // RI → reverse index
                            _actions.EscDispatch((byte)'M', _intermediates); break;
                    }
                    break;
                }
                // Check for UTF-8 multi-byte start
                if (b >= 0xC0 && b < 0xF8)
                {
                    if (b < 0xE0) { _utf8Remaining = 1; _utf8Codepoint = b & 0x1F; }
                    else if (b < 0xF0) { _utf8Remaining = 2; _utf8Codepoint = b & 0x0F; }
                    else { _utf8Remaining = 3; _utf8Codepoint = b & 0x07; }
                }
                else if (b >= 0x20 && b < 0x80)
                {
                    _actions.Print(new Rune(b));
                }
                // 0x80-0xBF and 0xF8+ are ignored/treated as continuation in Ground
                break;

            case State.Escape:
                if (b >= 0x20 && b <= 0x2F)
                {
                    _intermediates.Add(b);
                    TransitionTo(State.EscapeIntermediate);
                }
                else if (b == 0x5B) // '[' -> CSI
                {
                    TransitionTo(State.CsiEntry);
                }
                else if (b == 0x5D) // ']' -> OSC
                {
                    TransitionTo(State.OscString);
                }
                else if (b == 0x50) // 'P' -> DCS
                {
                    TransitionTo(State.DcsEntry);
                }
                else if (b == 0x58 || b == 0x5E || b == 0x5F) // SOS/PM/APC
                {
                    TransitionTo(State.SosPmApcString);
                }
                else if (b >= 0x30 && b <= 0x7E)
                {
                    _actions.EscDispatch(b, _intermediates);
                    TransitionTo(State.Ground);
                }
                break;

            case State.EscapeIntermediate:
                if (b >= 0x20 && b <= 0x2F)
                    _intermediates.Add(b);
                else if (b >= 0x30 && b <= 0x7E)
                {
                    _actions.EscDispatch(b, _intermediates);
                    TransitionTo(State.Ground);
                }
                break;

            case State.CsiEntry:
                if (b >= 0x30 && b <= 0x39) // digits
                {
                    _currentParam = b - '0';
                    TransitionTo(State.CsiParam);
                }
                else if (b == 0x3B) // ';'
                {
                    FinalizeParam();
                    TransitionTo(State.CsiParam);
                }
                else if (b >= 0x3C && b <= 0x3F) // private markers
                {
                    if (b == 0x3F) _isPrivate = true;
                    TransitionTo(State.CsiParam);
                }
                else if (b >= 0x20 && b <= 0x2F) // intermediates
                {
                    _intermediates.Add(b);
                    TransitionTo(State.CsiIntermediate);
                }
                else if (b >= 0x40 && b <= 0x7E) // final
                {
                    // Only add implicit param if one was explicitly started
                    if (_currentParam >= 0 || _params.Count > 0) FinalizeParam();
                    _actions.CsiDispatch(b, _params, _intermediates, _isPrivate);
                    TransitionTo(State.Ground);
                }
                break;

            case State.CsiParam:
                if (b >= 0x30 && b <= 0x39) // digits
                {
                    if (_currentParam < 0) _currentParam = 0;
                    _currentParam = _currentParam * 10 + (b - '0');
                }
                else if (b == 0x3B || b == 0x3A) // ';' or ':' (ISO 8613-6 colon sub-separator, e.g. tmux 38:2:R:G:B)
                {
                    FinalizeParam();
                }
                else if (b >= 0x3C && b <= 0x3F) // private in param = ignore -> CsiIgnore
                {
                    TransitionTo(State.CsiIgnore);
                }
                else if (b >= 0x20 && b <= 0x2F) // intermediates
                {
                    FinalizeParam();
                    _intermediates.Add(b);
                    TransitionTo(State.CsiIntermediate);
                }
                else if (b >= 0x40 && b <= 0x7E) // final
                {
                    FinalizeParam();
                    _actions.CsiDispatch(b, _params, _intermediates, _isPrivate);
                    TransitionTo(State.Ground);
                }
                break;

            case State.CsiIntermediate:
                if (b >= 0x20 && b <= 0x2F)
                    _intermediates.Add(b);
                else if (b >= 0x30 && b <= 0x3F)
                    TransitionTo(State.CsiIgnore);
                else if (b >= 0x40 && b <= 0x7E)
                {
                    FinalizeParam();
                    _actions.CsiDispatch(b, _params, _intermediates, _isPrivate);
                    TransitionTo(State.Ground);
                }
                break;

            case State.CsiIgnore:
                if (b >= 0x40 && b <= 0x7E)
                    TransitionTo(State.Ground);
                break;

            case State.OscString:
                if (b == 0x07) // BEL terminates
                {
                    _actions.OscDispatch(_oscBuffer.ToString());
                    TransitionTo(State.Ground);
                }
                else if (b == 0x1B)
                {
                    // ESC - check for ST (ESC \)
                    TransitionTo(State.OscEscPending);
                }
                else if (b >= 0x20 || b == 0x09 || b == 0x0A || b == 0x0D)
                {
                    _oscBuffer.Append((char)b);
                }
                break;

            case State.OscEscPending:
                if (b == 0x5C) // '\' - ST
                {
                    _actions.OscDispatch(_oscBuffer.ToString());
                    TransitionTo(State.Ground);
                }
                else
                {
                    // Not ST, process as new ESC sequence
                    TransitionTo(State.Escape);
                    ProcessByte(b);
                }
                break;

            case State.DcsEntry:
                if (b >= 0x30 && b <= 0x39)
                {
                    _currentParam = b - '0';
                    TransitionTo(State.DcsParam);
                }
                else if (b == 0x3B)
                {
                    FinalizeParam();
                    TransitionTo(State.DcsParam);
                }
                else if (b >= 0x3C && b <= 0x3F)
                {
                    if (b == 0x3F) _isPrivate = true;
                    TransitionTo(State.DcsParam);
                }
                else if (b >= 0x20 && b <= 0x2F)
                {
                    _intermediates.Add(b);
                    TransitionTo(State.DcsIntermediate);
                }
                else if (b >= 0x40 && b <= 0x7E)
                {
                    FinalizeParam();
                    _actions.DcsHook(b, _params, _intermediates);
                    TransitionTo(State.DcsPassthrough);
                }
                break;

            case State.DcsParam:
                if (b >= 0x30 && b <= 0x39)
                {
                    if (_currentParam < 0) _currentParam = 0;
                    _currentParam = _currentParam * 10 + (b - '0');
                }
                else if (b == 0x3B)
                    FinalizeParam();
                else if (b >= 0x20 && b <= 0x2F)
                {
                    FinalizeParam();
                    _intermediates.Add(b);
                    TransitionTo(State.DcsIntermediate);
                }
                else if (b >= 0x40 && b <= 0x7E)
                {
                    FinalizeParam();
                    _actions.DcsHook(b, _params, _intermediates);
                    TransitionTo(State.DcsPassthrough);
                }
                else if (b >= 0x3C && b <= 0x3F)
                    TransitionTo(State.DcsIgnore);
                break;

            case State.DcsIntermediate:
                if (b >= 0x20 && b <= 0x2F)
                    _intermediates.Add(b);
                else if (b >= 0x30 && b <= 0x3F)
                    TransitionTo(State.DcsIgnore);
                else if (b >= 0x40 && b <= 0x7E)
                {
                    FinalizeParam();
                    _actions.DcsHook(b, _params, _intermediates);
                    TransitionTo(State.DcsPassthrough);
                }
                break;

            case State.DcsPassthrough:
                // 0x9C (ST) or ESC \ terminates
                if (b >= 0x20 && b <= 0x7E)
                    _actions.DcsPut(b);
                else if (b == 0x9C)
                {
                    _actions.DcsUnhook();
                    TransitionTo(State.Ground);
                }
                break;

            case State.DcsIgnore:
                if (b == 0x9C)
                    TransitionTo(State.Ground);
                break;

            case State.SosPmApcString:
                if (b == 0x9C)
                    TransitionTo(State.Ground);
                break;
        }
    }

    private void FinalizeParam()
    {
        _params.Add(_currentParam); // -1 means default
        _currentParam = -1;
    }

    private void TransitionTo(State newState)
    {
        // Exit actions
        // (none needed for current implementation)

        // Entry actions
        switch (newState)
        {
            case State.Escape:
                _intermediates.Clear();
                _params.Clear();
                _isPrivate = false;
                _currentParam = -1;
                break;
            case State.CsiEntry:
                _intermediates.Clear();
                _params.Clear();
                _isPrivate = false;
                _currentParam = -1;
                break;
            case State.OscString:
                _oscBuffer.Clear();
                break;
            case State.DcsEntry:
                _intermediates.Clear();
                _params.Clear();
                _isPrivate = false;
                _currentParam = -1;
                break;
            case State.Ground:
                _utf8Remaining = 0;
                break;
        }

        _state = newState;
    }
}
