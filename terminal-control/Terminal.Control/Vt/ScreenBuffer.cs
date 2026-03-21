namespace Terminal.Vt;

using System.Text;

public sealed class ScreenBuffer
{
    private ScreenCell[] _mainCells;
    private ScreenCell[] _altCells;
    private ScreenCell[] _cells;

    private const int MaxScrollbackLines = 10000;
    private readonly List<(ScreenCell[] cells, bool softWrap)> _scrollback = new();

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    public int CursorRow { get; private set; }
    public int CursorCol { get; private set; }
    public bool CursorVisible { get; set; } = true;
    public CharacterAttributes CurrentAttributes { get; set; } = CharacterAttributes.Default;

    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }

    public bool AutoWrap { get; set; } = true;
    public bool OriginMode { get; set; }
    public bool UseAltBuffer { get; private set; }

    public int ScrollbackCount => _scrollback.Count;

    // Returns a scrollback row (idx=0 is oldest, idx=Count-1 is most recent scrolled-off)
    public ScreenCell[] GetScrollbackRow(int idx)
    {
        if (idx < 0 || idx >= _scrollback.Count)
        {
            var emptyRow = new ScreenCell[Columns];
            InitCells(emptyRow);
            return emptyRow;
        }
        return _scrollback[idx].cells;
    }

    public bool GetScrollbackRowSoftWrap(int idx) =>
        idx >= 0 && idx < _scrollback.Count && _scrollback[idx].softWrap;

    public void ClearScrollback()
    {
        _scrollback.Clear();
    }

    private bool _pendingWrap;
    private bool[] _dirty;
    private bool[] _rowSoftWrap; // true = row ended by auto-wrap (can rejoin on resize)

    private (int row, int col, CharacterAttributes attrs, bool originMode) _savedCursor;
    private (int row, int col, CharacterAttributes attrs, bool originMode) _altSavedCursor;

    public ScreenBuffer(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        _mainCells = new ScreenCell[columns * rows];
        _altCells = new ScreenCell[columns * rows];
        _cells = _mainCells;
        _dirty = new bool[rows];
        _rowSoftWrap = new bool[rows];
        ScrollBottom = rows - 1;
        InitCells(_mainCells);
        InitCells(_altCells);
        MarkAllDirty();
    }

    private static void InitCells(ScreenCell[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
            cells[i] = ScreenCell.Empty;
    }

    public void WriteRune(Rune r)
    {
        // Handle pending wrap
        if (_pendingWrap)
        {
            if (AutoWrap)
            {
                _rowSoftWrap[CursorRow] = true; // auto-wrap, not a hard newline
                CarriageReturn();
                DoLineFeed();
            }
            _pendingWrap = false;
        }

        EnsureCursorInBounds();

        // Determine width (CJK etc.)
        bool isWide = false;
        try { isWide = System.Globalization.StringInfo.GetNextTextElementLength(r.ToString()) > 0 && GetRuneWidth(r) == 2; }
        catch { }

        int idx = Index(CursorRow, CursorCol);
        if (idx >= 0 && idx < _cells.Length)
        {
            _cells[idx] = new ScreenCell
            {
                Character = r,
                Attributes = CurrentAttributes,
                IsWide = isWide,
                IsWideRight = false
            };
            MarkRowDirty(CursorRow);

            if (isWide && CursorCol + 1 < Columns)
            {
                int rightIdx = idx + 1;
                _cells[rightIdx] = new ScreenCell
                {
                    Character = new Rune(' '),
                    Attributes = CurrentAttributes,
                    IsWide = false,
                    IsWideRight = true
                };
            }
        }

        int advance = isWide ? 2 : 1;
        if (CursorCol + advance >= Columns)
        {
            CursorCol = Columns - 1;
            _pendingWrap = true;
        }
        else
        {
            CursorCol += advance;
        }
    }

    private static int GetRuneWidth(Rune r)
    {
        // Simple wide char detection
        int cp = r.Value;
        if ((cp >= 0x1100 && cp <= 0x115F) ||
            (cp >= 0x2E80 && cp <= 0x303E) ||
            (cp >= 0x3040 && cp <= 0x33FF) ||
            (cp >= 0x3400 && cp <= 0x4DBF) ||
            (cp >= 0x4E00 && cp <= 0x9FFF) ||
            (cp >= 0xA000 && cp <= 0xA4CF) ||
            (cp >= 0xAC00 && cp <= 0xD7AF) ||
            (cp >= 0xF900 && cp <= 0xFAFF) ||
            (cp >= 0xFE10 && cp <= 0xFE1F) ||
            (cp >= 0xFE30 && cp <= 0xFE6F) ||
            (cp >= 0xFF01 && cp <= 0xFF60) ||
            (cp >= 0xFFE0 && cp <= 0xFFE6) ||
            (cp >= 0x1F300 && cp <= 0x1FAFF))
            return 2;
        return 1;
    }

    public void WriteChar(char c) => WriteRune(new Rune(c));

    public void SetCursorPosition(int row, int col)
    {
        int minRow = OriginMode ? ScrollTop : 0;
        int maxRow = OriginMode ? ScrollBottom : Rows - 1;
        CursorRow = Math.Clamp(row + minRow, minRow, maxRow);
        CursorCol = Math.Clamp(col, 0, Columns - 1);
        _pendingWrap = false;
    }

    public void MoveCursorRow(int delta)
    {
        CursorRow = Math.Clamp(CursorRow + delta, 0, Rows - 1);
        _pendingWrap = false;
    }

    public void MoveCursorCol(int delta)
    {
        CursorCol = Math.Clamp(CursorCol + delta, 0, Columns - 1);
        _pendingWrap = false;
    }

    public void SetCursorRow(int row)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        _pendingWrap = false;
    }

    public void SetCursorCol(int col)
    {
        CursorCol = Math.Clamp(col, 0, Columns - 1);
        _pendingWrap = false;
    }

    public void CarriageReturn()
    {
        CursorCol = 0;
        _pendingWrap = false;
    }

    public void LineFeed()
    {
        _rowSoftWrap[CursorRow] = false; // hard line ending
        DoLineFeed();
    }

    private void DoLineFeed()
    {
        if (CursorRow == ScrollBottom)
        {
            ScrollUp(1);
        }
        else if (CursorRow < Rows - 1)
        {
            CursorRow++;
        }
        _pendingWrap = false;
    }

    public void ReverseLineFeed()
    {
        if (CursorRow == ScrollTop)
        {
            ScrollDown(1);
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
        }
        _pendingWrap = false;
    }

    public void Tab(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            int nextTab = ((CursorCol / 8) + 1) * 8;
            CursorCol = Math.Min(nextTab, Columns - 1);
        }
        _pendingWrap = false;
    }

    public void Backspace()
    {
        if (_pendingWrap)
        {
            _pendingWrap = false;
            return;
        }
        if (CursorCol > 0)
            CursorCol--;
    }

    public void EraseDisplay(int mode)
    {
        EnsureCursorInBounds();
        ScreenCell fill = MakeBlankCell();
        switch (mode)
        {
            case 0: // cursor to end
                int startIdx0 = Index(CursorRow, CursorCol);
                FillRange(startIdx0, _cells.Length - startIdx0, fill);
                // Break incoming soft-wrap link into the first erased row
                if (CursorRow > 0) _rowSoftWrap[CursorRow - 1] = false;
                for (int r = CursorRow; r < Rows; r++) { _rowSoftWrap[r] = false; MarkRowDirty(r); }
                break;
            case 1: // start to cursor
                int endIdx1 = Index(CursorRow, CursorCol);
                FillRange(0, endIdx1 + 1, fill);
                for (int r = 0; r <= CursorRow; r++) { _rowSoftWrap[r] = false; MarkRowDirty(r); }
                break;
            case 2: // all
                FillRange(0, _cells.Length, fill);
                for (int r = 0; r < Rows; r++) _rowSoftWrap[r] = false;
                MarkAllDirty();
                break;
            case 3: // all + scrollback
                FillRange(0, _cells.Length, fill);
                ClearScrollback();
                for (int r = 0; r < Rows; r++) _rowSoftWrap[r] = false;
                MarkAllDirty();
                break;
        }
    }

    public void EraseLine(int mode)
    {
        EnsureCursorInBounds();
        ScreenCell fill = MakeBlankCell();
        switch (mode)
        {
            case 0: // cursor to end of line
                FillRange(Index(CursorRow, CursorCol), Columns - CursorCol, fill);
                break;
            case 1: // start to cursor
                FillRange(Index(CursorRow, 0), CursorCol + 1, fill);
                break;
            case 2: // whole line
                FillRange(Index(CursorRow, 0), Columns, fill);
                break;
        }
        // Erasing content invalidates auto-wrap state for this row
        _rowSoftWrap[CursorRow] = false;
        // Break any incoming soft-wrap link from the row above
        if (CursorRow > 0) _rowSoftWrap[CursorRow - 1] = false;
        MarkRowDirty(CursorRow);
    }

    public void EraseChars(int count)
    {
        EnsureCursorInBounds();
        int n = Math.Min(count, Columns - CursorCol);
        ScreenCell fill = MakeBlankCell();
        FillRange(Index(CursorRow, CursorCol), n, fill);
        MarkRowDirty(CursorRow);
    }

    public void InsertLines(int count)
    {
        EnsureCursorInBounds();
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom) return;
        count = Math.Min(count, ScrollBottom - CursorRow + 1);
        ScreenCell fill = MakeBlankCell();
        // Shift rows [CursorRow..ScrollBottom-count] down by count
        for (int row = ScrollBottom; row >= CursorRow + count; row--)
        {
            Array.Copy(_cells, Index(row - count, 0), _cells, Index(row, 0), Columns);
            _rowSoftWrap[row] = _rowSoftWrap[row - count];
        }
        // Fill [CursorRow..CursorRow+count-1] with blanks
        for (int row = CursorRow; row < CursorRow + count; row++)
        {
            FillRange(Index(row, 0), Columns, fill);
            _rowSoftWrap[row] = false;
        }
        MarkAllDirty();
    }

    public void DeleteLines(int count)
    {
        EnsureCursorInBounds();
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom) return;
        count = Math.Min(count, ScrollBottom - CursorRow + 1);
        ScreenCell fill = MakeBlankCell();
        // Shift rows [CursorRow+count..ScrollBottom] up by count
        for (int row = CursorRow; row <= ScrollBottom - count; row++)
        {
            Array.Copy(_cells, Index(row + count, 0), _cells, Index(row, 0), Columns);
            _rowSoftWrap[row] = _rowSoftWrap[row + count];
        }
        // Fill bottom rows with blanks
        for (int row = ScrollBottom - count + 1; row <= ScrollBottom; row++)
        {
            FillRange(Index(row, 0), Columns, fill);
            _rowSoftWrap[row] = false;
        }
        MarkAllDirty();
    }

    public void InsertChars(int count)
    {
        EnsureCursorInBounds();
        count = Math.Min(count, Columns - CursorCol);
        ScreenCell fill = MakeBlankCell();
        int rowBase = Index(CursorRow, 0);
        // Shift chars right
        for (int col = Columns - 1; col >= CursorCol + count; col--)
            _cells[rowBase + col] = _cells[rowBase + col - count];
        // Fill inserted positions
        for (int col = CursorCol; col < CursorCol + count; col++)
            _cells[rowBase + col] = fill;
        MarkRowDirty(CursorRow);
    }

    public void DeleteChars(int count)
    {
        EnsureCursorInBounds();
        count = Math.Min(count, Columns - CursorCol);
        ScreenCell fill = MakeBlankCell();
        int rowBase = Index(CursorRow, 0);
        // Shift chars left
        for (int col = CursorCol; col < Columns - count; col++)
            _cells[rowBase + col] = _cells[rowBase + col + count];
        // Fill end positions
        for (int col = Columns - count; col < Columns; col++)
            _cells[rowBase + col] = fill;
        MarkRowDirty(CursorRow);
    }

    public void ScrollUp(int count)
    {
        count = Math.Min(count, ScrollBottom - ScrollTop + 1);

        // Save lines scrolling off the top to scrollback (main buffer only)
        if (!UseAltBuffer && ScrollTop == 0)
        {
            int saveCount = Math.Min(count, Rows);
            for (int i = 0; i < saveCount; i++)
            {
                var rowData = new ScreenCell[Columns];
                Array.Copy(_cells, i * Columns, rowData, 0, Columns);
                _scrollback.Add((rowData, _rowSoftWrap[i]));
            }
            // Trim to max size (remove oldest)
            while (_scrollback.Count > MaxScrollbackLines)
                _scrollback.RemoveAt(0);
        }

        ScreenCell fill = MakeBlankCell();
        for (int row = ScrollTop; row <= ScrollBottom - count; row++)
        {
            Array.Copy(_cells, Index(row + count, 0), _cells, Index(row, 0), Columns);
            _rowSoftWrap[row] = _rowSoftWrap[row + count];
        }
        for (int row = ScrollBottom - count + 1; row <= ScrollBottom; row++)
        {
            FillRange(Index(row, 0), Columns, fill);
            _rowSoftWrap[row] = false;
        }
        MarkAllDirty();
    }

    public void ScrollDown(int count)
    {
        count = Math.Min(count, ScrollBottom - ScrollTop + 1);
        ScreenCell fill = MakeBlankCell();
        // Move rows [ScrollTop..ScrollBottom-count] down by count, carrying softWrap flags
        for (int row = ScrollBottom; row >= ScrollTop + count; row--)
        {
            Array.Copy(_cells, Index(row - count, 0), _cells, Index(row, 0), Columns);
            _rowSoftWrap[row] = _rowSoftWrap[row - count];
        }
        // Fill top rows with blanks and clear their softWrap flags
        for (int row = ScrollTop; row < ScrollTop + count; row++)
        {
            FillRange(Index(row, 0), Columns, fill);
            _rowSoftWrap[row] = false;
        }
        MarkAllDirty();
    }

    public void SetScrollRegion(int top1based, int bottom1based)
    {
        int t = Math.Clamp(top1based - 1, 0, Rows - 1);
        int b = Math.Clamp(bottom1based - 1, 0, Rows - 1);
        if (t < b) { ScrollTop = t; ScrollBottom = b; }
        // Move cursor to home
        CursorRow = OriginMode ? ScrollTop : 0;
        CursorCol = 0;
        _pendingWrap = false;
    }

    public void ResetScrollRegion()
    {
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }

    public void SwitchToAltBuffer()
    {
        if (UseAltBuffer) return;
        _altSavedCursor = (CursorRow, CursorCol, CurrentAttributes, OriginMode);
        UseAltBuffer = true;
        _cells = _altCells;
        // Clear alt screen
        InitCells(_altCells);
        MarkAllDirty();
    }

    public void SwitchToMainBuffer()
    {
        if (!UseAltBuffer) return;
        UseAltBuffer = false;
        _cells = _mainCells;
        // Restore cursor
        CursorRow = Math.Clamp(_altSavedCursor.row, 0, Rows - 1);
        CursorCol = Math.Clamp(_altSavedCursor.col, 0, Columns - 1);
        CurrentAttributes = _altSavedCursor.attrs;
        OriginMode = _altSavedCursor.originMode;
        _pendingWrap = false;
        MarkAllDirty();
    }

    public void SaveCursor()
    {
        _savedCursor = (CursorRow, CursorCol, CurrentAttributes, OriginMode);
    }

    public void RestoreCursor()
    {
        CursorRow = Math.Clamp(_savedCursor.row, 0, Rows - 1);
        CursorCol = Math.Clamp(_savedCursor.col, 0, Columns - 1);
        CurrentAttributes = _savedCursor.attrs;
        OriginMode = _savedCursor.originMode;
        _pendingWrap = false;
    }

    public void Reset()
    {
        UseAltBuffer = false;
        _cells = _mainCells;
        InitCells(_mainCells);
        InitCells(_altCells);
        CursorRow = 0;
        CursorCol = 0;
        CurrentAttributes = CharacterAttributes.Default;
        AutoWrap = true;
        OriginMode = false;
        ResetScrollRegion();
        _pendingWrap = false;
        MarkAllDirty();
    }

    public void Resize(int newCols, int newRows)
    {
        if (newCols == Columns && newRows == Rows) return;

        var newAlt = new ScreenCell[newCols * newRows];
        InitCells(newAlt);

        if (UseAltBuffer)
        {
            // Simple copy for alt buffer — full-screen apps (htop, vim) redraw via SIGWINCH
            var newMain = new ScreenCell[newCols * newRows];
            InitCells(newMain);
            int copyCols = Math.Min(Columns, newCols);
            int copyRows = Math.Min(Rows, newRows);
            for (int row = 0; row < copyRows; row++)
                for (int col = 0; col < copyCols; col++)
                    newMain[row * newCols + col] = _mainCells[row * Columns + col];
            Columns = newCols;
            Rows = newRows;
            _mainCells = newMain;
            _altCells = newAlt;
            _cells = _altCells;
            _dirty = new bool[newRows];
            _rowSoftWrap = new bool[newRows];
            CursorRow = Math.Clamp(CursorRow, 0, Rows - 1);
            CursorCol = Math.Clamp(CursorCol, 0, Columns - 1);
            ScrollTop = 0;
            ScrollBottom = Rows - 1;
            _pendingWrap = false;
            MarkAllDirty();
            return;
        }

        // ---- Reflow main buffer + scrollback ----

        // Step A: Build logical lines from BOTH scrollback and main buffer (oldest first).
        // Soft-wrapped rows are joined with the next row into one logical line.
        var logLines = new List<ScreenCell[]>();

        int cursorLogLine = -1;
        int cursorLogOffset = 0;

        var accumCells = new List<ScreenCell>();
        int accumPhysRows = 0;

        // Include existing scrollback rows
        for (int i = 0; i < _scrollback.Count; i++)
        {
            var (sbCells, sbSoftWrap) = _scrollback[i];
            for (int col = 0; col < Columns; col++)
                accumCells.Add(col < sbCells.Length ? sbCells[col] : ScreenCell.Empty);
            accumPhysRows++;
            if (!sbSoftWrap)
            {
                logLines.Add(accumCells.ToArray());
                accumCells = new List<ScreenCell>();
                accumPhysRows = 0;
            }
        }

        // Include main buffer rows
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
                accumCells.Add(_mainCells[row * Columns + col]);
            accumPhysRows++;

            if (row == CursorRow && cursorLogLine < 0)
            {
                cursorLogLine = logLines.Count;
                cursorLogOffset = (accumPhysRows - 1) * Columns + CursorCol;
            }

            bool isSoftWrap = row < _rowSoftWrap.Length && _rowSoftWrap[row];
            if (!isSoftWrap)
            {
                logLines.Add(accumCells.ToArray());
                accumCells = new List<ScreenCell>();
                accumPhysRows = 0;
            }
        }
        // Flush any remaining accumulation (last logical line if buffer ended mid-soft-wrap)
        if (accumCells.Count > 0)
        {
            if (cursorLogLine < 0)
            {
                cursorLogLine = logLines.Count;
                cursorLogOffset = (accumPhysRows - 1) * Columns + CursorCol;
            }
            logLines.Add(accumCells.ToArray());
        }
        if (cursorLogLine < 0) { cursorLogLine = Math.Max(0, logLines.Count - 1); cursorLogOffset = 0; }

        // Step B: Compute where each logical line lands in the new layout.
        // Trim trailing blank cells first so empty rows don't expand when reflowing.
        var lineNewStartRow = new int[logLines.Count];
        var linePhysRows = new int[logLines.Count];
        int totalNewPhysRows = 0;
        for (int li = 0; li < logLines.Count; li++)
        {
            lineNewStartRow[li] = totalNewPhysRows;
            int contentLen = logLines[li].Length;
            while (contentLen > 0 && logLines[li][contentLen - 1].Character.Value == ' '
                   && !logLines[li][contentLen - 1].IsWide && !logLines[li][contentLen - 1].IsWideRight)
                contentLen--;

            // Rows with any non-default background color (e.g. MicroK8s MOTD separator/highlight
            // rows) must NOT be split across multiple physical rows: wrapping creates phantom
            // "white line" artefacts where the trailing colored-bg spaces land on a new row.
            // Instead, keep them as exactly one physical row — they get truncated on shrink
            // and extended by the background-fill logic in Step E on expand.
            bool hasColoredBg = false;
            for (int ci = 0; ci < logLines[li].Length; ci++)
            {
                var (_, effBg) = logLines[li][ci].Attributes.EffectiveColors;
                if (effBg != TerminalColor.DefaultBg) { hasColoredBg = true; break; }
            }

            int physRows = (contentLen == 0 || hasColoredBg) ? 1
                           : (contentLen + newCols - 1) / newCols;
            linePhysRows[li] = physRows;
            totalNewPhysRows += physRows;
        }

        // Step C: Compute new cursor position
        int newCursorRow = 0, newCursorCol = 0;
        if (cursorLogLine < logLines.Count)
        {
            // Clamp to the last valid cell in the new physical layout (not the raw logical line length).
            // Without this, a cursor in a blank trailing area can overflow into the next logical line.
            int maxValidOffset = linePhysRows[cursorLogLine] * newCols - 1;
            int clampedOffset = logLines[cursorLogLine].Length == 0 ? 0
                : Math.Min(cursorLogOffset, Math.Max(0, maxValidOffset));
            newCursorRow = lineNewStartRow[cursorLogLine] + clampedOffset / newCols;
            newCursorCol = clampedOffset % newCols;
        }

        // Step D: Anchor viewport to cursor position.
        // If cursor fits in the first newRows rows, viewportStart=0 (no offset needed).
        // Otherwise, show cursor at the bottom of the display.
        int viewportStart = newCursorRow >= newRows ? newCursorRow - newRows + 1 : 0;
        viewportStart = Math.Max(0, viewportStart);

        // Step E: Rebuild scrollback from scratch and fill new main buffer
        _scrollback.Clear();
        var newMain2 = new ScreenCell[newCols * newRows];
        var newSoftWrap = new bool[newRows];
        InitCells(newMain2);

        for (int li = 0; li < logLines.Count; li++)
        {
            var cells = logLines[li];
            int physRowsForLine = linePhysRows[li]; // use precomputed value (based on trimmed content)
            int lineGlobalStart = lineNewStartRow[li];

            for (int chunk = 0; chunk < physRowsForLine; chunk++)
            {
                int globalRow = lineGlobalStart + chunk;
                int destRow = globalRow - viewportStart;
                bool isLastChunk = (chunk == physRowsForLine - 1);
                int srcOffset = chunk * newCols;
                int take = Math.Min(newCols, cells.Length - srcOffset);

                // When the source row is shorter than newCols (terminal expanded), check if
                // we should extend the last cell's background across the remaining columns.
                // This preserves full-width colored backgrounds (e.g. MOTD separator rows).
                ScreenCell? bgFill = null;
                if (take > 0 && take < newCols)
                {
                    var lastCell = cells[srcOffset + take - 1];
                    if (lastCell.Character.Value == ' ' && !lastCell.IsWide)
                    {
                        var (_, effBg) = lastCell.Attributes.EffectiveColors;
                        if (effBg != TerminalColor.DefaultBg)
                        {
                            // Create a plain blank cell whose stored Background equals the
                            // effective background (de-reverses ReverseVideo if present).
                            bgFill = new ScreenCell
                            {
                                Character = new Rune(' '),
                                Attributes = new CharacterAttributes { Background = effBg }
                            };
                        }
                    }
                }

                if (destRow < 0)
                {
                    var sbRow = new ScreenCell[newCols];
                    InitCells(sbRow);
                    for (int c = 0; c < take; c++)
                        sbRow[c] = cells[srcOffset + c];
                    if (bgFill.HasValue)
                        for (int c = take; c < newCols; c++)
                            sbRow[c] = bgFill.Value;
                    _scrollback.Add((sbRow, !isLastChunk));
                    while (_scrollback.Count > MaxScrollbackLines)
                        _scrollback.RemoveAt(0);
                }
                else if (destRow < newRows)
                {
                    for (int c = 0; c < take; c++)
                        newMain2[destRow * newCols + c] = cells[srcOffset + c];
                    if (bgFill.HasValue)
                        for (int c = take; c < newCols; c++)
                            newMain2[destRow * newCols + c] = bgFill.Value;
                    newSoftWrap[destRow] = !isLastChunk;
                }
            }
        }

        // Step F: Apply
        newCursorRow -= viewportStart;
        Columns = newCols;
        Rows = newRows;
        _mainCells = newMain2;
        _altCells = newAlt;
        _cells = _mainCells;
        _dirty = new bool[newRows];
        _rowSoftWrap = newSoftWrap;
        CursorRow = Math.Clamp(newCursorRow, 0, Rows - 1);
        CursorCol = Math.Clamp(newCursorCol, 0, Columns - 1);
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        _pendingWrap = false;
        MarkAllDirty();
    }

    public ref ScreenCell GetCell(int row, int col)
    {
        int idx = Index(row, col);
        return ref _cells[idx];
    }

    public ScreenCell GetCellCopy(int row, int col)
    {
        int idx = Index(row, col);
        if (idx < 0 || idx >= _cells.Length) return ScreenCell.Empty;
        return _cells[idx];
    }

    public bool IsRowDirty(int row) => row >= 0 && row < _dirty.Length && _dirty[row];
    public void ClearDirty(int row) { if (row >= 0 && row < _dirty.Length) _dirty[row] = false; }
    public void MarkAllDirty() { for (int i = 0; i < _dirty.Length; i++) _dirty[i] = true; }
    public void MarkRowDirty(int row) { if (row >= 0 && row < _dirty.Length) _dirty[row] = true; }
    public bool GetRowSoftWrap(int row) => row >= 0 && row < _rowSoftWrap.Length && _rowSoftWrap[row];

    private int Index(int row, int col) => row * Columns + col;

    private void FillRange(int startIdx, int count, ScreenCell fill)
    {
        int end = Math.Min(startIdx + count, _cells.Length);
        for (int i = startIdx; i < end; i++)
            _cells[i] = fill;
    }

    private ScreenCell MakeBlankCell()
    {
        var attrs = new CharacterAttributes
        {
            Background = CurrentAttributes.Background
        };
        return new ScreenCell { Character = new Rune(' '), Attributes = attrs };
    }

    private void EnsureCursorInBounds()
    {
        CursorRow = Math.Clamp(CursorRow, 0, Rows - 1);
        CursorCol = Math.Clamp(CursorCol, 0, Columns - 1);
    }
}
