using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace PowerTerminal.Controls
{
    /// <summary>
    /// 2D character cell buffer emulating a VT100/xterm terminal screen.
    /// Supports cursor tracking, scrolling regions, alternate screen buffer, and scrollback.
    /// </summary>
    internal sealed class TerminalBuffer
    {
        // ── Cell data ────────────────────────────────────────────────────────

        internal struct CellStyle
        {
            public Brush Foreground;
            public Brush Background;
            public bool IsBold;
            public bool IsDim;
            public bool IsItalic;
            public bool IsUnderline;
            public bool IsInverse;
            public bool IsStrikethrough;
            public bool IsBlink;
            public bool IsHidden;
            public Brush UnderlineColor;

            public bool IsNonDefault =>
                Foreground != null || Background != null || UnderlineColor != null ||
                IsBold || IsDim || IsItalic || IsUnderline || IsInverse ||
                IsStrikethrough || IsBlink || IsHidden;

            public void Reset()
            {
                Foreground = null;
                Background = null;
                UnderlineColor = null;
                IsBold = false;
                IsDim = false;
                IsItalic = false;
                IsUnderline = false;
                IsInverse = false;
                IsStrikethrough = false;
                IsBlink = false;
                IsHidden = false;
            }
        }

        internal struct Cell
        {
            public char Character;
            public CellStyle Style;
        }

        // ── State ────────────────────────────────────────────────────────────

        private int _rows;
        private int _cols;
        private Cell[,] _cells;

        // Cursor (0-based)
        private int _cursorRow;
        private int _cursorCol;

        // Scrolling region (0-based, inclusive)
        private int _scrollTop;
        private int _scrollBottom;

        // Saved cursor (DECSC / DECRC)
        private int _savedRow;
        private int _savedCol;
        private CellStyle _savedStyle;

        // Alternate buffer
        private Cell[,] _altCells;
        private int _altCursorRow;
        private int _altCursorCol;
        private int _altScrollTop;
        private int _altScrollBottom;
        private bool _isAltBuffer;

        // Scrollback (primary buffer only)
        private readonly List<Cell[]> _scrollback = new();
        private const int MaxScrollback = 5000;

        // Current style applied to new characters
        public CellStyle CurrentStyle;

        // Mode flags
        public bool AutoWrap { get; set; } = true;
        public bool CursorVisible { get; set; } = true;
        public bool ApplicationCursorKeys { get; set; }
        public bool BracketedPasteMode { get; set; }
        public bool OriginMode { get; set; }
        public bool InsertMode { get; set; }
        public bool FocusReporting { get; set; }
        public bool SynchronizedOutput { get; set; }

        /// <summary>Mouse tracking mode. 0=off, 9=X10, 1000=normal, 1002=button, 1003=any.</summary>
        public int MouseMode { get; set; }
        /// <summary>Mouse encoding format. 0=normal (X10), 1006=SGR, 1015=URXVT.</summary>
        public int MouseEncoding { get; set; }

        /// <summary>Cursor shape: 0=default(block blink), 1=block blink, 2=block steady, 3=underline blink, 4=underline steady, 5=bar blink, 6=bar steady.</summary>
        public int CursorShape { get; set; }

        // Custom tab stops (null means use default 8-column stops)
        private HashSet<int>? _tabStops;

        // Last written character for REP (CSI b)
        internal char LastWrittenChar { get; private set; }

        // Saved DEC private modes for CSI ? … s / CSI ? … r
        private readonly Dictionary<int, bool> _savedDecModes = new();

        // Track if the cursor wrapped and is pending on the next column
        private bool _wrapPending;

        // Dirty tracking for rendering
        public bool IsDirty { get; set; } = true;

        // ── Properties ───────────────────────────────────────────────────────

        public int Rows => _rows;
        public int Cols => _cols;
        public int CursorRow => _cursorRow;
        public int CursorCol => _cursorCol;
        public bool IsAlternateBuffer => _isAltBuffer;
        public IReadOnlyList<Cell[]> Scrollback => _scrollback;

        public Cell GetCell(int row, int col) => _cells[row, col];

        // ── Construction ─────────────────────────────────────────────────────

        public TerminalBuffer(int rows, int cols)
        {
            _rows = Math.Max(rows, 1);
            _cols = Math.Max(cols, 1);
            _cells = new Cell[_rows, _cols];
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
            ClearRegion(0, _rows - 1, 0, _cols - 1);
        }

        // ── Character output ─────────────────────────────────────────────────

        /// <summary>Write a printable character at the cursor and advance.</summary>
        public void WriteChar(char c)
        {
            if (_wrapPending)
            {
                // Deferred wrap: move to start of next line
                _wrapPending = false;
                _cursorCol = 0;
                IndexDown();
            }

            if (_cursorCol >= _cols)
                _cursorCol = _cols - 1;

            // Insert mode: shift characters right before writing
            if (InsertMode)
            {
                for (int col = _cols - 1; col > _cursorCol; col--)
                    _cells[_cursorRow, col] = _cells[_cursorRow, col - 1];
            }

            _cells[_cursorRow, _cursorCol] = new Cell
            {
                Character = c,
                Style = CurrentStyle
            };

            LastWrittenChar = c;
            _cursorCol++;
            if (_cursorCol >= _cols)
            {
                if (AutoWrap)
                {
                    // Don't wrap immediately — defer until the next character
                    _wrapPending = true;
                    _cursorCol = _cols - 1; // keep cursor in bounds
                }
                else
                {
                    _cursorCol = _cols - 1;
                }
            }

            IsDirty = true;
        }

        /// <summary>Line Feed — move cursor down, scroll if at bottom of scrolling region.</summary>
        public void LineFeed()
        {
            _wrapPending = false;
            IndexDown();
            IsDirty = true;
        }

        /// <summary>Carriage Return — move cursor to column 0.</summary>
        public void CarriageReturn()
        {
            _wrapPending = false;
            _cursorCol = 0;
            IsDirty = true;
        }

        /// <summary>Backspace — move cursor left by one if possible.</summary>
        public void Backspace()
        {
            _wrapPending = false;
            if (_cursorCol > 0) _cursorCol--;
            IsDirty = true;
        }

        /// <summary>Horizontal Tab — advance to next tab stop.</summary>
        public void Tab()
        {
            _wrapPending = false;
            if (_tabStops != null && _tabStops.Count > 0)
            {
                // Use custom tab stops
                int next = _cols - 1;
                foreach (int stop in _tabStops)
                {
                    if (stop > _cursorCol && stop < next)
                        next = stop;
                }
                // If no custom stop found after cursor, find smallest stop >= cols
                bool found = false;
                foreach (int stop in _tabStops)
                {
                    if (stop > _cursorCol)
                    {
                        if (!found || stop < next) next = stop;
                        found = true;
                    }
                }
                _cursorCol = found ? Math.Min(next, _cols - 1) : _cols - 1;
            }
            else
            {
                // Default 8-column stops
                int next = ((_cursorCol / 8) + 1) * 8;
                _cursorCol = Math.Min(next, _cols - 1);
            }
            IsDirty = true;
        }

        /// <summary>HTS (ESC H) — set a tab stop at the current cursor column.</summary>
        public void SetTabStop()
        {
            _tabStops ??= new HashSet<int>();
            // Seed default stops on first custom set
            if (_tabStops.Count == 0)
                for (int c = 8; c < _cols; c += 8)
                    _tabStops.Add(c);
            _tabStops.Add(_cursorCol);
        }

        /// <summary>TBC (CSI 0 g) — clear the tab stop at the current column.</summary>
        public void ClearTabStop()
        {
            _tabStops?.Remove(_cursorCol);
        }

        /// <summary>TBC (CSI 3 g) — clear all tab stops.</summary>
        public void ClearAllTabStops()
        {
            _tabStops?.Clear();
        }

        /// <summary>Bell — no-op for visual terminal.</summary>
        public void Bell() { }

        // ── Cursor movement ──────────────────────────────────────────────────

        /// <summary>CUP / HVP — set cursor position (0-based row, col). Respects origin mode.</summary>
        public void SetCursorPosition(int row, int col)
        {
            _wrapPending = false;
            if (OriginMode)
            {
                // In origin mode, row 0 is _scrollTop
                row += _scrollTop;
                _cursorRow = Math.Clamp(row, _scrollTop, _scrollBottom);
            }
            else
            {
                _cursorRow = Math.Clamp(row, 0, _rows - 1);
            }
            _cursorCol = Math.Clamp(col, 0, _cols - 1);
            IsDirty = true;
        }

        /// <summary>CUU — cursor up n rows.</summary>
        public void CursorUp(int n)
        {
            _wrapPending = false;
            _cursorRow = Math.Max(_cursorRow - n, _scrollTop);
            IsDirty = true;
        }

        /// <summary>CUD — cursor down n rows.</summary>
        public void CursorDown(int n)
        {
            _wrapPending = false;
            _cursorRow = Math.Min(_cursorRow + n, _scrollBottom);
            IsDirty = true;
        }

        /// <summary>CUF — cursor forward n columns.</summary>
        public void CursorForward(int n)
        {
            _wrapPending = false;
            _cursorCol = Math.Min(_cursorCol + n, _cols - 1);
            IsDirty = true;
        }

        /// <summary>CUB — cursor backward n columns.</summary>
        public void CursorBackward(int n)
        {
            _wrapPending = false;
            _cursorCol = Math.Max(_cursorCol - n, 0);
            IsDirty = true;
        }

        /// <summary>CNL — cursor to beginning of line n lines down.</summary>
        public void CursorNextLine(int n)
        {
            _wrapPending = false;
            _cursorRow = Math.Min(_cursorRow + n, _scrollBottom);
            _cursorCol = 0;
            IsDirty = true;
        }

        /// <summary>CPL — cursor to beginning of line n lines up.</summary>
        public void CursorPreviousLine(int n)
        {
            _wrapPending = false;
            _cursorRow = Math.Max(_cursorRow - n, _scrollTop);
            _cursorCol = 0;
            IsDirty = true;
        }

        /// <summary>CHA — cursor to column n (0-based).</summary>
        public void CursorToColumn(int col)
        {
            _wrapPending = false;
            _cursorCol = Math.Clamp(col, 0, _cols - 1);
            IsDirty = true;
        }

        /// <summary>VPA — cursor to row n (0-based), column unchanged.</summary>
        public void CursorToRow(int row)
        {
            _wrapPending = false;
            _cursorRow = Math.Clamp(row, 0, _rows - 1);
            IsDirty = true;
        }

        // ── Erase operations ─────────────────────────────────────────────────

        /// <summary>ED — erase in display. mode: 0=below, 1=above, 2=all, 3=all+scrollback.</summary>
        public void EraseDisplay(int mode)
        {
            switch (mode)
            {
                case 0: // Erase below (cursor to end)
                    ClearRegion(_cursorRow, _cursorRow, _cursorCol, _cols - 1);
                    if (_cursorRow + 1 < _rows)
                        ClearRegion(_cursorRow + 1, _rows - 1, 0, _cols - 1);
                    break;
                case 1: // Erase above (start to cursor)
                    if (_cursorRow > 0)
                        ClearRegion(0, _cursorRow - 1, 0, _cols - 1);
                    ClearRegion(_cursorRow, _cursorRow, 0, _cursorCol);
                    break;
                case 2: // Erase all
                    ClearRegion(0, _rows - 1, 0, _cols - 1);
                    break;
                case 3: // Erase all + scrollback
                    ClearRegion(0, _rows - 1, 0, _cols - 1);
                    _scrollback.Clear();
                    break;
            }
            IsDirty = true;
        }

        /// <summary>EL — erase in line. mode: 0=right, 1=left, 2=all.</summary>
        public void EraseLine(int mode)
        {
            switch (mode)
            {
                case 0: // Right of cursor (inclusive)
                    ClearRegion(_cursorRow, _cursorRow, _cursorCol, _cols - 1);
                    break;
                case 1: // Left of cursor (inclusive)
                    ClearRegion(_cursorRow, _cursorRow, 0, _cursorCol);
                    break;
                case 2: // Entire line
                    ClearRegion(_cursorRow, _cursorRow, 0, _cols - 1);
                    break;
            }
            IsDirty = true;
        }

        /// <summary>ECH — erase n characters from cursor position.</summary>
        public void EraseCharacters(int n)
        {
            int end = Math.Min(_cursorCol + n - 1, _cols - 1);
            ClearRegion(_cursorRow, _cursorRow, _cursorCol, end);
            IsDirty = true;
        }

        // ── Line operations ──────────────────────────────────────────────────

        /// <summary>IL — insert n blank lines at cursor row, scrolling down.</summary>
        public void InsertLines(int n)
        {
            if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
            for (int i = 0; i < n; i++)
                ScrollDownInRegion(_cursorRow, _scrollBottom);
            IsDirty = true;
        }

        /// <summary>DL — delete n lines at cursor row, scrolling up.</summary>
        public void DeleteLines(int n)
        {
            if (_cursorRow < _scrollTop || _cursorRow > _scrollBottom) return;
            for (int i = 0; i < n; i++)
                ScrollUpInRegion(_cursorRow, _scrollBottom);
            IsDirty = true;
        }

        /// <summary>ICH — insert n blank characters at cursor, shifting right.</summary>
        public void InsertCharacters(int n)
        {
            int row = _cursorRow;
            int col = _cursorCol;
            // Shift characters right
            for (int c = _cols - 1; c >= col + n; c--)
                _cells[row, c] = _cells[row, c - n];
            // Clear inserted positions
            for (int c = col; c < Math.Min(col + n, _cols); c++)
                _cells[row, c] = new Cell { Character = ' ' };
            IsDirty = true;
        }

        /// <summary>DCH — delete n characters at cursor, shifting left.</summary>
        public void DeleteCharacters(int n)
        {
            int row = _cursorRow;
            int col = _cursorCol;
            // Shift characters left
            for (int c = col; c + n < _cols; c++)
                _cells[row, c] = _cells[row, c + n];
            // Clear vacated positions at the end
            for (int c = Math.Max(col, _cols - n); c < _cols; c++)
                _cells[row, c] = new Cell { Character = ' ' };
            IsDirty = true;
        }

        // ── Scrolling ────────────────────────────────────────────────────────

        /// <summary>SU — scroll up n lines within the scrolling region.</summary>
        public void ScrollUp(int n)
        {
            for (int i = 0; i < n; i++)
                ScrollUpInRegion(_scrollTop, _scrollBottom);
            IsDirty = true;
        }

        /// <summary>SD — scroll down n lines within the scrolling region.</summary>
        public void ScrollDown(int n)
        {
            for (int i = 0; i < n; i++)
                ScrollDownInRegion(_scrollTop, _scrollBottom);
            IsDirty = true;
        }

        /// <summary>IND (ESC D) — move cursor down; scroll if at bottom of region.</summary>
        public void IndexDown()
        {
            if (_cursorRow == _scrollBottom)
            {
                ScrollUpInRegion(_scrollTop, _scrollBottom);
            }
            else if (_cursorRow < _rows - 1)
            {
                _cursorRow++;
            }
            IsDirty = true;
        }

        /// <summary>RI (ESC M) — move cursor up; scroll down if at top of region.</summary>
        public void ReverseIndex()
        {
            if (_cursorRow == _scrollTop)
            {
                ScrollDownInRegion(_scrollTop, _scrollBottom);
            }
            else if (_cursorRow > 0)
            {
                _cursorRow--;
            }
            IsDirty = true;
        }

        /// <summary>DECSTBM — set scrolling region (0-based, inclusive).</summary>
        public void SetScrollRegion(int top, int bottom)
        {
            if (top < 0) top = 0;
            if (bottom >= _rows) bottom = _rows - 1;
            if (top >= bottom) return; // invalid
            _scrollTop = top;
            _scrollBottom = bottom;
            // Cursor moves to home position per spec
            _cursorRow = 0;
            _cursorCol = 0;
            _wrapPending = false;
            IsDirty = true;
        }

        /// <summary>Reset scrolling region to full screen.</summary>
        public void ResetScrollRegion()
        {
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
            IsDirty = true;
        }

        // ── Save / Restore cursor ────────────────────────────────────────────

        public void SaveCursor()
        {
            _savedRow = _cursorRow;
            _savedCol = _cursorCol;
            _savedStyle = CurrentStyle;
        }

        public void RestoreCursor()
        {
            _cursorRow = Math.Clamp(_savedRow, 0, _rows - 1);
            _cursorCol = Math.Clamp(_savedCol, 0, _cols - 1);
            CurrentStyle = _savedStyle;
            _wrapPending = false;
            IsDirty = true;
        }

        // ── Alternate buffer ─────────────────────────────────────────────────

        /// <summary>Switch to alternate screen buffer (saves primary state).</summary>
        public void SwitchToAlternateBuffer()
        {
            if (_isAltBuffer) return;
            _isAltBuffer = true;

            // Save primary buffer state
            _altCells = _cells;
            _altCursorRow = _cursorRow;
            _altCursorCol = _cursorCol;
            _altScrollTop = _scrollTop;
            _altScrollBottom = _scrollBottom;

            // Create fresh alternate buffer
            _cells = new Cell[_rows, _cols];
            ClearRegion(0, _rows - 1, 0, _cols - 1);
            _cursorRow = 0;
            _cursorCol = 0;
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
            _wrapPending = false;
            IsDirty = true;
        }

        /// <summary>Switch back to primary screen buffer (restores state).</summary>
        public void SwitchToNormalBuffer()
        {
            if (!_isAltBuffer) return;
            _isAltBuffer = false;

            // Restore primary buffer state
            _cells = _altCells ?? new Cell[_rows, _cols];
            _cursorRow = Math.Clamp(_altCursorRow, 0, _rows - 1);
            _cursorCol = Math.Clamp(_altCursorCol, 0, _cols - 1);
            _scrollTop = _altScrollTop;
            _scrollBottom = Math.Min(_altScrollBottom, _rows - 1);
            _altCells = null;
            _wrapPending = false;
            IsDirty = true;
        }

        // ── Resize ───────────────────────────────────────────────────────────

        public void Resize(int newRows, int newCols)
        {
            if (newRows < 1) newRows = 1;
            if (newCols < 1) newCols = 1;
            if (newRows == _rows && newCols == _cols) return;

            var newCells = new Cell[newRows, newCols];
            // Initialize with spaces
            for (int r = 0; r < newRows; r++)
                for (int c = 0; c < newCols; c++)
                    newCells[r, c].Character = ' ';

            // Copy existing content
            int copyRows = Math.Min(_rows, newRows);
            int copyCols = Math.Min(_cols, newCols);
            for (int r = 0; r < copyRows; r++)
                for (int c = 0; c < copyCols; c++)
                    newCells[r, c] = _cells[r, c];

            _cells = newCells;
            _rows = newRows;
            _cols = newCols;
            _cursorRow = Math.Clamp(_cursorRow, 0, _rows - 1);
            _cursorCol = Math.Clamp(_cursorCol, 0, _cols - 1);
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
            _wrapPending = false;
            IsDirty = true;
        }

        /// <summary>Full terminal reset.</summary>
        public void FullReset()
        {
            _cursorRow = 0;
            _cursorCol = 0;
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
            _wrapPending = false;
            CurrentStyle.Reset();
            AutoWrap = true;
            CursorVisible = true;
            ApplicationCursorKeys = false;
            BracketedPasteMode = false;
            OriginMode = false;
            InsertMode = false;
            FocusReporting = false;
            SynchronizedOutput = false;
            MouseMode = 0;
            MouseEncoding = 0;
            CursorShape = 0;
            _tabStops = null;
            LastWrittenChar = '\0';
            _savedDecModes.Clear();
            ClearRegion(0, _rows - 1, 0, _cols - 1);
            _scrollback.Clear();
            if (_isAltBuffer) SwitchToNormalBuffer();
            IsDirty = true;
        }

        /// <summary>DECSTR (CSI ! p) — soft reset. Resets modes but keeps screen content.</summary>
        public void SoftReset()
        {
            _cursorRow = 0;
            _cursorCol = 0;
            _scrollTop = 0;
            _scrollBottom = _rows - 1;
            _wrapPending = false;
            CurrentStyle.Reset();
            AutoWrap = true;
            CursorVisible = true;
            ApplicationCursorKeys = false;
            BracketedPasteMode = false;
            OriginMode = false;
            InsertMode = false;
            MouseMode = 0;
            MouseEncoding = 0;
            CursorShape = 0;
            _tabStops = null;
            LastWrittenChar = '\0';
            IsDirty = true;
        }

        /// <summary>REP (CSI b) — repeat the last written character n times.</summary>
        public void RepeatLastChar(int n)
        {
            if (LastWrittenChar == '\0') return;
            for (int i = 0; i < n; i++)
                WriteChar(LastWrittenChar);
        }

        /// <summary>DECALN (ESC # 8) — fill entire screen with 'E' characters for alignment test.</summary>
        public void FillWithE()
        {
            CurrentStyle.Reset();
            for (int r = 0; r < _rows; r++)
                for (int c = 0; c < _cols; c++)
                    _cells[r, c] = new Cell { Character = 'E' };
            _cursorRow = 0;
            _cursorCol = 0;
            _wrapPending = false;
            IsDirty = true;
        }

        /// <summary>Save a DEC private mode value for later restore.</summary>
        public void SaveDecMode(int mode, bool value)
        {
            _savedDecModes[mode] = value;
        }

        /// <summary>Restore a previously saved DEC private mode value. Returns null if not saved.</summary>
        public bool? RestoreDecMode(int mode)
        {
            return _savedDecModes.TryGetValue(mode, out var val) ? val : null;
        }

        /// <summary>Get the current value of a DEC private mode by number.</summary>
        public bool GetDecMode(int mode)
        {
            return mode switch
            {
                1 => ApplicationCursorKeys,
                6 => OriginMode,
                7 => AutoWrap,
                25 => CursorVisible,
                1004 => FocusReporting,
                2004 => BracketedPasteMode,
                _ => false
            };
        }

        // ── Internal helpers ─────────────────────────────────────────────────

        private void ClearRegion(int rowStart, int rowEnd, int colStart, int colEnd)
        {
            rowEnd = Math.Min(rowEnd, _rows - 1);
            colEnd = Math.Min(colEnd, _cols - 1);
            for (int r = rowStart; r <= rowEnd; r++)
                for (int c = colStart; c <= colEnd; c++)
                {
                    _cells[r, c].Character = ' ';
                    _cells[r, c].Style = default;
                }
        }

        /// <summary>Scroll lines up within the given region. Top line goes to scrollback (primary only).</summary>
        private void ScrollUpInRegion(int top, int bottom)
        {
            // Save top line to scrollback if this is the primary buffer and region is the full screen top
            if (!_isAltBuffer && top == 0)
            {
                var saved = new Cell[_cols];
                for (int c = 0; c < _cols; c++)
                    saved[c] = _cells[0, c];
                _scrollback.Add(saved);
                if (_scrollback.Count > MaxScrollback)
                    _scrollback.RemoveAt(0);
            }

            // Shift rows up
            for (int r = top; r < bottom; r++)
                for (int c = 0; c < _cols; c++)
                    _cells[r, c] = _cells[r + 1, c];

            // Clear bottom row
            for (int c = 0; c < _cols; c++)
            {
                _cells[bottom, c].Character = ' ';
                _cells[bottom, c].Style = default;
            }
        }

        /// <summary>Scroll lines down within the given region. Bottom line is discarded.</summary>
        private void ScrollDownInRegion(int top, int bottom)
        {
            for (int r = bottom; r > top; r--)
                for (int c = 0; c < _cols; c++)
                    _cells[r, c] = _cells[r - 1, c];

            // Clear top row
            for (int c = 0; c < _cols; c++)
            {
                _cells[top, c].Character = ' ';
                _cells[top, c].Style = default;
            }
        }
    }
}
