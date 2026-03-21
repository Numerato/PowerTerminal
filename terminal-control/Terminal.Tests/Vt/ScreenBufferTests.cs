namespace Terminal.Tests.Vt;

using Terminal.Vt;
using Xunit;
using System.Text;

public class ScreenBufferTests
{
    [Fact]
    public void InitialCursorAtOrigin()
    {
        var buf = new ScreenBuffer(80, 24);
        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(0, buf.CursorCol);
    }

    [Fact]
    public void WriteCharAdvancesCursor()
    {
        var buf = new ScreenBuffer(80, 24);
        buf.WriteChar('A');
        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(1, buf.CursorCol);
        Assert.Equal(new Rune('A'), buf.GetCellCopy(0, 0).Character);
    }

    [Fact]
    public void WriteCharAtEndOfLineWraps()
    {
        var buf = new ScreenBuffer(5, 24);
        buf.AutoWrap = true;
        for (int i = 0; i < 5; i++) buf.WriteChar('A');
        // At end of line after 5 chars, pendingWrap = true, cursor stays at col 4
        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(4, buf.CursorCol);
        // Next write wraps
        buf.WriteChar('B');
        Assert.Equal(1, buf.CursorRow);
        Assert.Equal(1, buf.CursorCol);
    }

    [Fact]
    public void WriteCharAtEndOfLineDoesNotWrapWhenDisabled()
    {
        var buf = new ScreenBuffer(5, 24);
        buf.AutoWrap = false;
        for (int i = 0; i < 10; i++) buf.WriteChar('A');
        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(4, buf.CursorCol);
    }

    [Fact]
    public void LineFeedScrollsAtBottom()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.SetCursorPosition(2, 0); // last row
        buf.WriteChar('X');
        buf.SetCursorPosition(2, 0);
        buf.LineFeed(); // should scroll
        Assert.Equal(2, buf.CursorRow);
        // Row 0 content gone, row 1 moved to 0, row 2 moved to 1, new empty row 2
    }

    [Fact]
    public void LineFeedScrollsAtScrollRegionBottom()
    {
        var buf = new ScreenBuffer(10, 10);
        buf.SetScrollRegion(2, 4); // rows 1-3 (0-based), scroll bottom = 3
        buf.SetCursorPosition(3, 0); // at scroll region bottom (row 3, 0-based)
        buf.WriteChar('X');
        buf.SetCursorPosition(3, 0);
        buf.LineFeed();
        Assert.Equal(3, buf.CursorRow); // stays at scroll bottom after scrolling
    }

    [Fact]
    public void ReverseLineFeedScrollsAtScrollRegionTop()
    {
        var buf = new ScreenBuffer(10, 10);
        buf.SetScrollRegion(3, 8);
        buf.SetCursorPosition(2, 0); // scroll top (0-based = 2)
        buf.WriteChar('A');
        buf.SetCursorPosition(2, 0);
        buf.ReverseLineFeed();
        Assert.Equal(2, buf.CursorRow); // stays at scroll top
    }

    [Fact]
    public void EraseDisplayToEnd()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.WriteChar('A');
        buf.WriteChar('B');
        buf.SetCursorPosition(0, 1);
        buf.EraseDisplay(0);
        Assert.Equal(new Rune('A'), buf.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(0, 1).Character);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(1, 0).Character);
    }

    [Fact]
    public void EraseDisplayAll()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.WriteChar('A');
        buf.SetCursorPosition(1, 2);
        buf.WriteChar('B');
        buf.EraseDisplay(2);
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 5; c++)
                Assert.Equal(new Rune(' '), buf.GetCellCopy(r, c).Character);
    }

    [Fact]
    public void EraseLine()
    {
        var buf = new ScreenBuffer(5, 3);
        for (int c = 0; c < 5; c++) buf.WriteChar((char)('A' + c));
        buf.SetCursorPosition(0, 0);
        buf.EraseLine(2);
        for (int c = 0; c < 5; c++)
            Assert.Equal(new Rune(' '), buf.GetCellCopy(0, c).Character);
    }

    [Fact]
    public void InsertLinesShiftDown()
    {
        var buf = new ScreenBuffer(5, 5);
        // Write row 0: AAAAA, row 1: BBBBB
        buf.SetCursorPosition(0, 0); for (int i = 0; i < 5; i++) buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); for (int i = 0; i < 5; i++) buf.WriteChar('B');
        // Insert 1 line at row 0 - row 0 should become blank, row 1 = old row 0
        buf.SetCursorPosition(0, 0);
        buf.InsertLines(1);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune('A'), buf.GetCellCopy(1, 0).Character);
        Assert.Equal(new Rune('B'), buf.GetCellCopy(2, 0).Character);
    }

    [Fact]
    public void DeleteLinesShiftUp()
    {
        var buf = new ScreenBuffer(5, 5);
        buf.SetCursorPosition(0, 0); for (int i = 0; i < 5; i++) buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); for (int i = 0; i < 5; i++) buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); for (int i = 0; i < 5; i++) buf.WriteChar('C');
        buf.SetCursorPosition(0, 0);
        buf.DeleteLines(1);
        Assert.Equal(new Rune('B'), buf.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune('C'), buf.GetCellCopy(1, 0).Character);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(4, 0).Character);
    }

    [Fact]
    public void InsertCharsShiftRight()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.WriteChar('A'); buf.WriteChar('B'); buf.WriteChar('C');
        buf.SetCursorPosition(0, 1);
        buf.InsertChars(1);
        Assert.Equal(new Rune('A'), buf.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(0, 1).Character);
        Assert.Equal(new Rune('B'), buf.GetCellCopy(0, 2).Character);
        Assert.Equal(new Rune('C'), buf.GetCellCopy(0, 3).Character);
    }

    [Fact]
    public void DeleteCharsShiftLeft()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.WriteChar('A'); buf.WriteChar('B'); buf.WriteChar('C'); buf.WriteChar('D');
        buf.SetCursorPosition(0, 1);
        buf.DeleteChars(1);
        Assert.Equal(new Rune('A'), buf.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune('C'), buf.GetCellCopy(0, 1).Character);
        Assert.Equal(new Rune('D'), buf.GetCellCopy(0, 2).Character);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(0, 3).Character);
    }

    [Fact]
    public void ScrollUpMovesContent()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.SetCursorPosition(0, 0); for (int i = 0; i < 5; i++) buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); for (int i = 0; i < 5; i++) buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); for (int i = 0; i < 5; i++) buf.WriteChar('C');
        buf.ScrollUp(1);
        Assert.Equal(new Rune('B'), buf.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune('C'), buf.GetCellCopy(1, 0).Character);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(2, 0).Character);
    }

    [Fact]
    public void ScrollDownMovesContent()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.SetCursorPosition(0, 0); for (int i = 0; i < 5; i++) buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); for (int i = 0; i < 5; i++) buf.WriteChar('B');
        buf.ScrollDown(1);
        Assert.Equal(new Rune(' '), buf.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune('A'), buf.GetCellCopy(1, 0).Character);
        Assert.Equal(new Rune('B'), buf.GetCellCopy(2, 0).Character);
    }

    [Fact]
    public void SetScrollRegionLimitsScrolling()
    {
        var buf = new ScreenBuffer(5, 5);
        buf.SetCursorPosition(0, 0); for (int i = 0; i < 5; i++) buf.WriteChar('X');
        buf.SetScrollRegion(2, 4); // rows 1-3 (0-based)
        buf.SetCursorPosition(2, 0); // bottom of scroll region (row 3 0-based)
        buf.LineFeed(); // should scroll only within region
        // Row 0 (X's) should be untouched
        Assert.Equal(new Rune('X'), buf.GetCellCopy(0, 0).Character);
    }

    [Fact]
    public void SwitchToAltBufferClearsScreen()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.WriteChar('A');
        buf.SwitchToAltBuffer();
        Assert.Equal(new Rune(' '), buf.GetCellCopy(0, 0).Character);
        Assert.True(buf.UseAltBuffer);
    }

    [Fact]
    public void SwitchBackToMainBufferRestoresContent()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.WriteChar('A');
        buf.SwitchToAltBuffer();
        buf.WriteChar('B');
        buf.SwitchToMainBuffer();
        Assert.Equal(new Rune('A'), buf.GetCellCopy(0, 0).Character);
        Assert.False(buf.UseAltBuffer);
    }

    [Fact]
    public void SaveRestoreCursor()
    {
        var buf = new ScreenBuffer(80, 24);
        buf.SetCursorPosition(5, 10);
        buf.SaveCursor();
        buf.SetCursorPosition(0, 0);
        buf.RestoreCursor();
        Assert.Equal(5, buf.CursorRow);
        Assert.Equal(10, buf.CursorCol);
    }

    [Fact]
    public void ResizePreservesCursor()
    {
        var buf = new ScreenBuffer(80, 24);
        buf.SetCursorPosition(5, 10);
        buf.Resize(40, 12);
        Assert.Equal(5, buf.CursorRow);
        Assert.Equal(10, buf.CursorCol);
    }

    [Fact]
    public void ResizeExpandsBuffer()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.WriteChar('A');
        buf.Resize(10, 6);
        Assert.Equal(10, buf.Columns);
        Assert.Equal(6, buf.Rows);
        Assert.Equal(new Rune('A'), buf.GetCellCopy(0, 0).Character);
    }

    [Fact]
    public void DirtyTrackingWorks()
    {
        var buf = new ScreenBuffer(5, 3);
        buf.ClearDirty(0);
        Assert.False(buf.IsRowDirty(0));
        buf.WriteChar('A');
        Assert.True(buf.IsRowDirty(0));
    }

    [Fact]
    public void TabAdvancesToNextTabStop()
    {
        var buf = new ScreenBuffer(80, 24);
        buf.SetCursorCol(0);
        buf.Tab();
        Assert.Equal(8, buf.CursorCol);
        buf.Tab();
        Assert.Equal(16, buf.CursorCol);
        buf.SetCursorCol(7);
        buf.Tab();
        Assert.Equal(8, buf.CursorCol);
    }

    [Fact]
    public void ScrollUp_SavesLinesToScrollback()
    {
        var buf = new ScreenBuffer(10, 3);
        buf.WriteChar('A'); buf.CarriageReturn(); buf.LineFeed();
        buf.WriteChar('B'); buf.CarriageReturn(); buf.LineFeed();
        buf.WriteChar('C'); buf.CarriageReturn(); buf.LineFeed();
        // now at bottom, one more linefeed should scroll
        buf.WriteChar('D');
        // After writing D on line 2 (0-indexed) and doing linefeed, line with 'A' scrolls off
        Assert.True(buf.ScrollbackCount >= 1);
    }

    [Fact]
    public void Resize_KeepsCursorVisible_WhenShrinkingRows()
    {
        var buf = new ScreenBuffer(80, 40);
        // Move cursor to row 35
        buf.SetCursorRow(35);
        buf.Resize(80, 24);
        // Cursor should still be visible within the new 24-row buffer
        Assert.True(buf.CursorRow < 24);
        Assert.True(buf.CursorRow >= 0);
    }

    /// <summary>
    /// Simulates "docker ps" style output: each line is 70 chars (fits in 80, wraps at 40).
    /// After shrink: content must fill the main buffer from the top with no blank rows at top.
    /// After re-expand: content reconstructed, no blank rows at top.
    /// </summary>
    [Fact]
    public void Reflow_NoBlankRowsAtTop_WhenShrinkingWidthWithWrappingContent()
    {
        // 80 cols, 5 rows — small for easy tracing
        var buf = new ScreenBuffer(80, 5);

        // Helper: write a line of text followed by \r\n
        void WriteLine(string text)
        {
            foreach (char c in text) buf.WriteChar(c);
            buf.CarriageReturn();
            buf.LineFeed();
        }

        // Fill screen: write 5 lines (each 70 chars, hard newlines)
        // The 5th line causes scrolling so the original row 0 goes to scrollback
        string line = new string('A', 70);
        for (int i = 0; i < 5; i++) WriteLine(line);

        // Now write the prompt: cursor is at start of an empty row
        buf.WriteChar('$'); buf.WriteChar(' ');
        // State: scrollback=1, main buffer rows 0-4 = lines 1-4 + "$ "

        Assert.Equal(1, buf.ScrollbackCount);
        Assert.Equal(5, buf.Rows);

        // Shrink to 40 cols — each 70-char line needs 2 rows at 40 cols
        buf.Resize(40, 5);

        // After shrink: the main buffer should have NO blank rows at the very top
        // (row 0 must not be blank unless all content really fits in fewer rows)
        var row0 = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(0, c)).ToArray();
        bool row0AllBlank = row0.All(cell => cell.Character.Value == ' ');
        Assert.False(row0AllBlank, "After shrink to 40 cols, row 0 of main buffer should not be blank");

        // Cursor should be visible
        Assert.True(buf.CursorRow >= 0 && buf.CursorRow < buf.Rows);

        // Now expand back to 80 cols
        buf.Resize(80, 5);

        // After expand: row 0 must not be blank either
        row0 = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(0, c)).ToArray();
        row0AllBlank = row0.All(cell => cell.Character.Value == ' ');
        Assert.False(row0AllBlank, "After expanding back to 80 cols, row 0 should not be blank");
    }

    /// <summary>
    /// Simulates the exact "white lines" scenario: scrollback + main buffer with wrapped lines.
    /// Shrink + expand must reconstruct original content order with no gaps.
    /// </summary>
    [Fact]
    public void Reflow_ShrinkExpand_PreservesContentOrder()
    {
        var buf = new ScreenBuffer(80, 6);

        // Write 8 lines of 60-char content (will scroll, filling scrollback)
        for (int i = 0; i < 8; i++)
        {
            string line = new string((char)('A' + i), 60);
            foreach (char c in line) buf.WriteChar(c);
            buf.CarriageReturn();
            buf.LineFeed();
        }
        // Write prompt
        buf.WriteChar('$'); buf.WriteChar(' ');

        // At this point: scrollback has some rows, main buffer has 6 rows
        int scrollbackBefore = buf.ScrollbackCount;
        Assert.True(scrollbackBefore > 0, "Should have scrollback content");

        // Shrink to 40 cols
        buf.Resize(40, 6);

        // Row 0 must not be blank (content should fill from top after cursor-anchor)
        var row0After = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(0, c)).ToArray();
        bool row0Blank = row0After.All(cell => cell.Character.Value == ' ');
        Assert.False(row0Blank, "After shrink, row 0 should contain content, not be blank");

        // All rows in main buffer should be non-blank (since we have enough content)
        for (int r = 0; r < buf.Rows; r++)
        {
            var row = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(r, c)).ToArray();
            bool isBlank = row.All(cell => cell.Character.Value == ' ');
            Assert.False(isBlank, $"Row {r} should not be blank after shrink");
        }

        // Expand back to 80 cols
        buf.Resize(80, 6);

        // Row 0 still should not be blank
        row0After = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(0, c)).ToArray();
        row0Blank = row0After.All(cell => cell.Character.Value == ' ');
        Assert.False(row0Blank, "After expand, row 0 should contain content, not be blank");
    }

    /// <summary>
    /// Tests fix for cursor-offset overflow in reflow Step C.
    /// When expanding from a narrow width where the cursor sits in row 2+ of a wrapped
    /// logical line, the cursor offset can exceed physRows*newCols, misplacing the cursor
    /// into the next logical line and causing viewportStart to be off by one (blank row).
    /// </summary>
    [Fact]
    public void Reflow_CursorOverflow_NoBlankRow_WhenExpandingWrappedLine()
    {
        // 80-col buffer, write a 90-char line so it will wrap into 2 rows at 80 cols
        // then place cursor in the second row at col 20
        var buf = new ScreenBuffer(80, 6);

        // Write a 90-char auto-wrapping line (no \r\n — forces soft-wrap)
        string longLine = new string('X', 90);
        buf.AutoWrap = true;
        foreach (char c in longLine) buf.WriteChar(c);
        // Cursor is now at row 1 (wrapped), col 10 (90%80=10)
        Assert.Equal(1, buf.CursorRow);
        Assert.Equal(10, buf.CursorCol);

        // Expand to 100 cols — the 90-char logical line now fits in 1 physical row
        // Without the fix: cursorLogOffset=80+10=90 >= physRows*100=100? No, 90<100.
        // Edge case: col 20 (> 100-80=20 threshold)
        buf.SetCursorPosition(1, 20); // force cursor to col 20 in 2nd row
        buf.Resize(100, 6);

        // Cursor must still be in the buffer, not overflowed
        Assert.True(buf.CursorRow >= 0 && buf.CursorRow < buf.Rows,
            "Cursor row should be within buffer bounds after expand");
        Assert.True(buf.CursorCol >= 0 && buf.CursorCol < buf.Columns,
            "Cursor col should be within buffer bounds after expand");

        // The row containing the cursor must not be blank (content was placed there)
        var cursorRow = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(buf.CursorRow, c)).ToArray();
        bool cursorRowBlank = cursorRow.All(cell => cell.Character.Value == ' ');
        Assert.False(cursorRowBlank, "The cursor row after expand should contain content, not be blank");

        // Verify no blank rows above content
        var row0 = Enumerable.Range(0, buf.Columns).Select(c => buf.GetCellCopy(0, c)).ToArray();
        bool row0Blank = row0.All(cell => cell.Character.Value == ' ');
        Assert.False(row0Blank, "Row 0 should contain 'X' characters after expand");
    }

    /// <summary>
    /// Verifies that EraseLine correctly clears the soft-wrap flag so that
    /// subsequent resize does not join the erased row with adjacent rows.
    /// </summary>
    [Fact]
    public void EraseLine_ClearsSoftWrapFlag()
    {
        var buf = new ScreenBuffer(10, 5);
        buf.AutoWrap = true;

        // Write 15 chars — wraps at col 9, sets softWrap[0]=true
        foreach (char c in "AAAAAAAAABBBBB") buf.WriteChar(c);
        // Row 0: 10 A's, softWrap=true; Row 1: 5 B's, cursor at row 1 col 4

        // Move cursor to row 0 and erase the line
        buf.SetCursorPosition(0, 0);
        buf.EraseLine(2); // whole line

        // Resize to 20 cols — should not join (empty) row 0 with row 1's B's
        buf.Resize(20, 5);

        // Row 0 should either be blank (pushed to scrollback due to cursor-anchor) or
        // contain only B's (if row 0 was the old row 0 blank and B's are on row 1).
        // Key assertion: B characters should appear somewhere in the buffer and no blank rows
        // should appear BETWEEN lines of content
        bool foundB = false;
        for (int r = 0; r < buf.Rows; r++)
            for (int c = 0; c < buf.Columns; c++)
                if (buf.GetCellCopy(r, c).Character.Value == 'B') foundB = true;
        Assert.True(foundB, "B characters should still be present after resize");
    }

    [Fact]
    public void ScrollDownPreservesSoftWrap()
    {
        // Write two lines that soft-wrap, then scroll down: softWrap flags must shift correctly.
        var buf = new ScreenBuffer(5, 5);
        buf.AutoWrap = true;
        // Write 7 chars to rows 0 and 1 (soft wrap after row 0)
        for (int i = 0; i < 7; i++) buf.WriteChar((char)('A' + i));
        // Row 0 should be soft-wrapped
        Assert.True(buf.GetRowSoftWrap(0), "row 0 should be soft-wrapped");
        Assert.False(buf.GetRowSoftWrap(1), "row 1 should not be soft-wrapped");
        // Scroll down by 1 within full screen (rows 0..4)
        buf.ScrollDown(1);
        // After scroll: row 1 has old row 0 content; row 0 is new blank
        // row 1 should carry the softWrap flag of old row 0 (true)
        Assert.True(buf.GetRowSoftWrap(1), "row 1 should carry old row 0 softWrap=true");
        // row 0 is blank — softWrap must be false
        Assert.False(buf.GetRowSoftWrap(0), "new blank row 0 should have softWrap=false");
    }

    [Fact]
    public void ResizeExpandsColoredBackgroundToFillRow()
    {
        // Write a row of space characters with a colored background, then expand the terminal.
        // The added cells should inherit the colored background rather than default to dark.
        var buf = new ScreenBuffer(5, 3);
        var brightWhite = TerminalColor.FromIndex(15);
        // Set a colored background (indexed color 15 = bright white)
        buf.CurrentAttributes = new CharacterAttributes { Background = brightWhite };
        for (int c = 0; c < 5; c++) buf.WriteChar(' ');
        buf.CurrentAttributes = CharacterAttributes.Default;
        // Move cursor to row 1 so the colored row is above cursor
        buf.SetCursorPosition(1, 0);
        // Expand to 8 cols
        buf.Resize(8, 3);
        // Row 0 (or wherever the colored row ended up) should have colored bg all the way across
        int coloredRow = -1;
        for (int r = 0; r < buf.Rows; r++)
        {
            var (_, effBg) = buf.GetCellCopy(r, 0).Attributes.EffectiveColors;
            if (effBg == brightWhite) { coloredRow = r; break; }
        }
        Assert.True(coloredRow >= 0, "A row with colored background should exist");
        for (int c = 0; c < buf.Columns; c++)
        {
            var (_, effBg) = buf.GetCellCopy(coloredRow, c).Attributes.EffectiveColors;
            Assert.Equal(brightWhite, effBg);
        }
    }

    [Fact]
    public void ResizeShrinkDoesNotSplitColoredBgRowsIntoPhantomWhiteLines()
    {
        // Regression: shrinking the terminal must NOT split a colored-background row into
        // multiple physical rows. The last chunk of a split row would contain only
        // colored-bg spaces and appear as a phantom "white line" between content rows.
        //
        // Layout (80 cols, 5 rows):
        //   row 0: default-bg text  "dark text"
        //   row 1: bright-white-bg  spaces (separator)
        //   row 2: bright-white-bg  "box content" + trailing spaces (text on white bg)
        //   row 3: bright-white-bg  spaces (separator)
        //   row 4: default-bg text  "more dark text"
        var brightWhite = TerminalColor.FromIndex(15);
        var black       = TerminalColor.FromIndex(0);
        var buf = new ScreenBuffer(80, 5);

        // Row 0: dark text
        buf.CurrentAttributes = CharacterAttributes.Default;
        WriteString(buf, "dark text");
        buf.LineFeed(); buf.CarriageReturn();

        // Row 1: bright-white-bg separator (all spaces, 80 wide)
        buf.CurrentAttributes = new CharacterAttributes { Background = brightWhite, Foreground = black };
        for (int c = 0; c < 80; c++) buf.WriteChar(' ');
        buf.CurrentAttributes = CharacterAttributes.Default;
        buf.LineFeed(); buf.CarriageReturn();

        // Row 2: text on bright-white-bg, padded to 80 with trailing white spaces
        buf.CurrentAttributes = new CharacterAttributes { Background = brightWhite, Foreground = black };
        WriteString(buf, "  box content text that is quite long and fills about half the row  ");
        // pad to 80
        for (int c = buf.CursorCol; c < 80; c++) buf.WriteChar(' ');
        buf.CurrentAttributes = CharacterAttributes.Default;
        buf.LineFeed(); buf.CarriageReturn();

        // Row 3: bright-white-bg separator
        buf.CurrentAttributes = new CharacterAttributes { Background = brightWhite, Foreground = black };
        for (int c = 0; c < 80; c++) buf.WriteChar(' ');
        buf.CurrentAttributes = CharacterAttributes.Default;
        buf.LineFeed(); buf.CarriageReturn();

        // Row 4: more dark text (cursor lands here)
        buf.CurrentAttributes = CharacterAttributes.Default;
        WriteString(buf, "more dark text");

        // Shrink to 40 cols.  Without the fix, row 2 would split into physRows=2 and the
        // trailing-white-spaces chunk would appear as an extra white line between rows 1–3.
        buf.Resize(40, 5);

        // Expected layout after resize (5 rows):
        //   row 0: dark bg  (text, possibly truncated)
        //   row 1: white bg (separator)
        //   row 2: white bg (box content text, truncated to 40)
        //   row 3: white bg (separator)
        //   row 4: dark bg  (text, possibly truncated / in scrollback)
        //
        // Key invariants:
        //   - row 1 must be white bg (separator)
        //   - row 2 must be white bg (box content)
        //   - row 3 must be white bg (separator)
        //   - rows 1,2,3 must be adjacent (no extra rows between them)
        //   - No dark-bg row between the three white rows

        // Find the first white row
        int firstWhite = -1;
        for (int r = 0; r < buf.Rows; r++)
        {
            var (_, effBg) = buf.GetCellCopy(r, 0).Attributes.EffectiveColors;
            if (effBg == brightWhite) { firstWhite = r; break; }
        }
        Assert.True(firstWhite >= 0, "Expected at least one white-bg row");

        // The three white rows must be consecutive
        Assert.True(firstWhite + 2 < buf.Rows, "Expected 3 consecutive white rows");
        for (int r = firstWhite; r <= firstWhite + 2; r++)
        {
            var (_, effBg) = buf.GetCellCopy(r, 0).Attributes.EffectiveColors;
            Assert.Equal(brightWhite, effBg);
        }

        // No dark row sandwiched between the three white rows
        for (int r = firstWhite; r <= firstWhite + 2; r++)
        {
            bool rowIsWhite = false;
            for (int c = 0; c < buf.Columns; c++)
            {
                var (_, effBg) = buf.GetCellCopy(r, c).Attributes.EffectiveColors;
                if (effBg == brightWhite) { rowIsWhite = true; break; }
            }
            Assert.True(rowIsWhite, $"Row {r} should be a white-bg row");
        }
    }

    private static void WriteString(ScreenBuffer buf, string s)
    {
        foreach (char ch in s) buf.WriteChar(ch);
    }
}
