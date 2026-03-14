using PowerTerminal.Controls;
using System.Windows.Media;

namespace PowerTerminal.Tests;

public class TerminalBufferTests
{
    private TerminalBuffer CreateBuffer(int rows = 24, int cols = 80)
        => new TerminalBuffer(rows, cols);

    // ── Basic character output ────────────────────────────────────────────

    [Fact]
    public void WriteChar_PlacesCharacterAtCursor()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('H');
        buf.WriteChar('i');

        Assert.Equal('H', buf.GetCell(0, 0).Character);
        Assert.Equal('i', buf.GetCell(0, 1).Character);
        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(2, buf.CursorCol);
    }

    [Fact]
    public void LineFeed_MovesCursorDown()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('A');
        buf.LineFeed();

        Assert.Equal(1, buf.CursorRow);
        Assert.Equal(1, buf.CursorCol); // Column unchanged
    }

    [Fact]
    public void CarriageReturn_MovesCursorToColumn0()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('A');
        buf.WriteChar('B');
        buf.CarriageReturn();

        Assert.Equal(0, buf.CursorCol);
    }

    [Fact]
    public void Backspace_MovesCursorLeft()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('A');
        buf.WriteChar('B');
        buf.Backspace();

        Assert.Equal(1, buf.CursorCol);
    }

    [Fact]
    public void Tab_AdvancesToNextTabStop()
    {
        var buf = CreateBuffer(5, 80);
        buf.WriteChar('A'); // col 1
        buf.Tab(); // should jump to col 8

        Assert.Equal(8, buf.CursorCol);
    }

    // ── Cursor movement ──────────────────────────────────────────────────

    [Fact]
    public void SetCursorPosition_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(5, 10);

        Assert.Equal(5, buf.CursorRow);
        Assert.Equal(10, buf.CursorCol);
    }

    [Fact]
    public void SetCursorPosition_Clamped()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(100, 100);

        Assert.Equal(9, buf.CursorRow);
        Assert.Equal(19, buf.CursorCol);
    }

    [Fact]
    public void CursorUp_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(5, 5);
        buf.CursorUp(3);

        Assert.Equal(2, buf.CursorRow);
        Assert.Equal(5, buf.CursorCol);
    }

    [Fact]
    public void CursorDown_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(5, 5);
        buf.CursorDown(3);

        Assert.Equal(8, buf.CursorRow);
    }

    [Fact]
    public void CursorForward_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.CursorForward(5);

        Assert.Equal(5, buf.CursorCol);
    }

    [Fact]
    public void CursorBackward_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(0, 10);
        buf.CursorBackward(3);

        Assert.Equal(7, buf.CursorCol);
    }

    [Fact]
    public void CursorNextLine_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(2, 5);
        buf.CursorNextLine(2);

        Assert.Equal(4, buf.CursorRow);
        Assert.Equal(0, buf.CursorCol);
    }

    [Fact]
    public void CursorPreviousLine_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(5, 10);
        buf.CursorPreviousLine(2);

        Assert.Equal(3, buf.CursorRow);
        Assert.Equal(0, buf.CursorCol);
    }

    [Fact]
    public void CursorToColumn_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.CursorToColumn(15);

        Assert.Equal(15, buf.CursorCol);
    }

    [Fact]
    public void CursorToRow_Works()
    {
        var buf = CreateBuffer(10, 20);
        buf.CursorToRow(7);

        Assert.Equal(7, buf.CursorRow);
    }

    // ── Erase operations ─────────────────────────────────────────────────

    [Fact]
    public void EraseLine_Right_ClearsFromCursor()
    {
        var buf = CreateBuffer(5, 10);
        for (int i = 0; i < 10; i++) buf.WriteChar((char)('A' + i));
        buf.SetCursorPosition(0, 5);
        buf.EraseLine(0); // Erase right

        // Characters 0-4 preserved, 5-9 cleared
        Assert.Equal('A', buf.GetCell(0, 0).Character);
        Assert.Equal('E', buf.GetCell(0, 4).Character);
        Assert.Equal(' ', buf.GetCell(0, 5).Character);
        Assert.Equal(' ', buf.GetCell(0, 9).Character);
    }

    [Fact]
    public void EraseLine_Left_ClearsToLeft()
    {
        var buf = CreateBuffer(5, 10);
        for (int i = 0; i < 10; i++) buf.WriteChar((char)('A' + i));
        buf.SetCursorPosition(0, 5);
        buf.EraseLine(1); // Erase left

        Assert.Equal(' ', buf.GetCell(0, 0).Character);
        Assert.Equal(' ', buf.GetCell(0, 5).Character);
        Assert.Equal('G', buf.GetCell(0, 6).Character);
    }

    [Fact]
    public void EraseLine_All_ClearsEntireLine()
    {
        var buf = CreateBuffer(5, 10);
        for (int i = 0; i < 10; i++) buf.WriteChar((char)('A' + i));
        buf.SetCursorPosition(0, 5);
        buf.EraseLine(2);

        for (int i = 0; i < 10; i++)
            Assert.Equal(' ', buf.GetCell(0, i).Character);
    }

    [Fact]
    public void EraseDisplay_All_ClearsScreen()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('X');
        buf.LineFeed();
        buf.WriteChar('Y');
        buf.EraseDisplay(2);

        Assert.Equal(' ', buf.GetCell(0, 0).Character);
        Assert.Equal(' ', buf.GetCell(1, 0).Character);
    }

    [Fact]
    public void EraseCharacters_ClearsNChars()
    {
        var buf = CreateBuffer(5, 10);
        for (int i = 0; i < 10; i++) buf.WriteChar((char)('A' + i));
        buf.SetCursorPosition(0, 2);
        buf.EraseCharacters(3);

        Assert.Equal('A', buf.GetCell(0, 0).Character);
        Assert.Equal('B', buf.GetCell(0, 1).Character);
        Assert.Equal(' ', buf.GetCell(0, 2).Character);
        Assert.Equal(' ', buf.GetCell(0, 3).Character);
        Assert.Equal(' ', buf.GetCell(0, 4).Character);
        Assert.Equal('F', buf.GetCell(0, 5).Character);
    }

    // ── Scrolling ────────────────────────────────────────────────────────

    [Fact]
    public void LineFeed_AtBottom_ScrollsUp()
    {
        var buf = CreateBuffer(3, 5);
        buf.SetCursorPosition(0, 0); buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); buf.WriteChar('C');

        // At the bottom, LF should scroll
        buf.LineFeed();

        Assert.Equal('B', buf.GetCell(0, 0).Character);
        Assert.Equal('C', buf.GetCell(1, 0).Character);
        Assert.Equal(' ', buf.GetCell(2, 0).Character);
    }

    [Fact]
    public void ScrollRegion_Works()
    {
        var buf = CreateBuffer(5, 10);
        buf.SetScrollRegion(1, 3); // Rows 1-3 scroll, 0 and 4 stay

        // Write content in region
        buf.SetCursorPosition(0, 0); buf.WriteChar('A'); // Outside region above
        buf.SetCursorPosition(1, 0); buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); buf.WriteChar('C');
        buf.SetCursorPosition(3, 0); buf.WriteChar('D');
        buf.SetCursorPosition(4, 0); buf.WriteChar('E'); // Outside region below

        // Scroll up within region
        buf.ScrollUp(1);

        Assert.Equal('A', buf.GetCell(0, 0).Character); // Unchanged
        Assert.Equal('C', buf.GetCell(1, 0).Character); // Shifted up
        Assert.Equal('D', buf.GetCell(2, 0).Character); // Shifted up
        Assert.Equal(' ', buf.GetCell(3, 0).Character); // Cleared
        Assert.Equal('E', buf.GetCell(4, 0).Character); // Unchanged
    }

    [Fact]
    public void ReverseIndex_AtTop_ScrollsDown()
    {
        var buf = CreateBuffer(3, 5);
        buf.SetCursorPosition(0, 0); buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); buf.WriteChar('C');

        buf.SetCursorPosition(0, 0);
        buf.ReverseIndex();

        Assert.Equal(' ', buf.GetCell(0, 0).Character);
        Assert.Equal('A', buf.GetCell(1, 0).Character);
        Assert.Equal('B', buf.GetCell(2, 0).Character);
    }

    // ── Line operations ──────────────────────────────────────────────────

    [Fact]
    public void InsertLines_Works()
    {
        var buf = CreateBuffer(5, 5);
        buf.SetCursorPosition(0, 0); buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); buf.WriteChar('C');
        buf.SetCursorPosition(3, 0); buf.WriteChar('D');
        buf.SetCursorPosition(4, 0); buf.WriteChar('E');

        buf.SetCursorPosition(1, 0);
        buf.InsertLines(1);

        Assert.Equal('A', buf.GetCell(0, 0).Character);
        Assert.Equal(' ', buf.GetCell(1, 0).Character); // Inserted blank
        Assert.Equal('B', buf.GetCell(2, 0).Character); // Shifted down
        Assert.Equal('C', buf.GetCell(3, 0).Character);
        Assert.Equal('D', buf.GetCell(4, 0).Character); // E fell off
    }

    [Fact]
    public void DeleteLines_Works()
    {
        var buf = CreateBuffer(5, 5);
        buf.SetCursorPosition(0, 0); buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); buf.WriteChar('C');
        buf.SetCursorPosition(3, 0); buf.WriteChar('D');
        buf.SetCursorPosition(4, 0); buf.WriteChar('E');

        buf.SetCursorPosition(1, 0);
        buf.DeleteLines(1);

        Assert.Equal('A', buf.GetCell(0, 0).Character);
        Assert.Equal('C', buf.GetCell(1, 0).Character);
        Assert.Equal('D', buf.GetCell(2, 0).Character);
        Assert.Equal('E', buf.GetCell(3, 0).Character);
        Assert.Equal(' ', buf.GetCell(4, 0).Character); // Cleared
    }

    [Fact]
    public void InsertCharacters_Works()
    {
        var buf = CreateBuffer(5, 10);
        for (int i = 0; i < 5; i++) buf.WriteChar((char)('A' + i)); // ABCDE
        buf.SetCursorPosition(0, 2);
        buf.InsertCharacters(2);

        Assert.Equal('A', buf.GetCell(0, 0).Character);
        Assert.Equal('B', buf.GetCell(0, 1).Character);
        Assert.Equal(' ', buf.GetCell(0, 2).Character);
        Assert.Equal(' ', buf.GetCell(0, 3).Character);
        Assert.Equal('C', buf.GetCell(0, 4).Character);
        Assert.Equal('D', buf.GetCell(0, 5).Character);
    }

    [Fact]
    public void DeleteCharacters_Works()
    {
        var buf = CreateBuffer(5, 10);
        for (int i = 0; i < 5; i++) buf.WriteChar((char)('A' + i)); // ABCDE
        buf.SetCursorPosition(0, 1);
        buf.DeleteCharacters(2);

        Assert.Equal('A', buf.GetCell(0, 0).Character);
        Assert.Equal('D', buf.GetCell(0, 1).Character);
        Assert.Equal('E', buf.GetCell(0, 2).Character);
        Assert.Equal(' ', buf.GetCell(0, 3).Character);
    }

    // ── Alternate buffer ─────────────────────────────────────────────────

    [Fact]
    public void AlternateBuffer_SwitchAndRestore()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('X');
        Assert.False(buf.IsAlternateBuffer);

        buf.SwitchToAlternateBuffer();
        Assert.True(buf.IsAlternateBuffer);
        Assert.Equal(' ', buf.GetCell(0, 0).Character); // Clean alternate

        buf.WriteChar('Y');
        Assert.Equal('Y', buf.GetCell(0, 0).Character);

        buf.SwitchToNormalBuffer();
        Assert.False(buf.IsAlternateBuffer);
        Assert.Equal('X', buf.GetCell(0, 0).Character); // Primary restored
    }

    // ── Save / Restore cursor ────────────────────────────────────────────

    [Fact]
    public void SaveRestore_Cursor()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetCursorPosition(5, 10);
        buf.SaveCursor();

        buf.SetCursorPosition(0, 0);
        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(0, buf.CursorCol);

        buf.RestoreCursor();
        Assert.Equal(5, buf.CursorRow);
        Assert.Equal(10, buf.CursorCol);
    }

    // ── Auto-wrap ────────────────────────────────────────────────────────

    [Fact]
    public void AutoWrap_WrapsAtEndOfLine()
    {
        var buf = CreateBuffer(5, 5);
        buf.AutoWrap = true;

        for (int i = 0; i < 5; i++) buf.WriteChar((char)('A' + i));
        // After writing 5 chars on a 5-col terminal, cursor should be at end
        // Next char should wrap to next line
        buf.WriteChar('F');

        Assert.Equal('F', buf.GetCell(1, 0).Character);
        Assert.Equal(1, buf.CursorRow);
    }

    [Fact]
    public void NoAutoWrap_StaysAtEnd()
    {
        var buf = CreateBuffer(5, 5);
        buf.AutoWrap = false;

        for (int i = 0; i < 7; i++) buf.WriteChar((char)('A' + i));

        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(4, buf.CursorCol); // Stays at last column
    }

    // ── Scrollback ───────────────────────────────────────────────────────

    [Fact]
    public void Scrollback_AccumulatesWhenScrollingPrimary()
    {
        var buf = CreateBuffer(3, 5);
        buf.SetCursorPosition(0, 0); buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); buf.WriteChar('C');

        // Scroll: 'A' should go into scrollback
        buf.LineFeed();

        Assert.Single(buf.Scrollback);
        Assert.Equal('A', buf.Scrollback[0][0].Character);
    }

    [Fact]
    public void Scrollback_NotAccumulatedInAlternateBuffer()
    {
        var buf = CreateBuffer(3, 5);
        buf.SwitchToAlternateBuffer();

        buf.SetCursorPosition(0, 0); buf.WriteChar('A');
        buf.SetCursorPosition(1, 0); buf.WriteChar('B');
        buf.SetCursorPosition(2, 0); buf.WriteChar('C');
        buf.LineFeed();

        Assert.Empty(buf.Scrollback);
    }

    // ── Resize ───────────────────────────────────────────────────────────

    [Fact]
    public void Resize_PreservesContent()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('X');
        buf.Resize(10, 20);

        Assert.Equal(10, buf.Rows);
        Assert.Equal(20, buf.Cols);
        Assert.Equal('X', buf.GetCell(0, 0).Character);
    }

    // ── Full reset ───────────────────────────────────────────────────────

    [Fact]
    public void FullReset_ClearsEverything()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('X');
        buf.SetCursorPosition(3, 5);
        buf.FullReset();

        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(0, buf.CursorCol);
        Assert.Equal(' ', buf.GetCell(0, 0).Character);
        Assert.True(buf.AutoWrap);
        Assert.True(buf.CursorVisible);
        Assert.False(buf.ApplicationCursorKeys);
    }

    // ── Style tracking ───────────────────────────────────────────────────

    [Fact]
    public void Style_AppliedToWrittenCells()
    {
        var buf = CreateBuffer(5, 10);
        var brush = new SolidColorBrush(Colors.Red);
        brush.Freeze();

        buf.CurrentStyle.Foreground = brush;
        buf.CurrentStyle.IsBold = true;
        buf.WriteChar('X');

        var cell = buf.GetCell(0, 0);
        Assert.Equal(brush, cell.Style.Foreground);
        Assert.True(cell.Style.IsBold);
    }

    // ── Insert Mode ──────────────────────────────────────────────────────

    [Fact]
    public void InsertMode_ShiftsCharactersRight()
    {
        var buf = CreateBuffer(5, 10);
        for (int i = 0; i < 5; i++) buf.WriteChar((char)('A' + i)); // ABCDE
        buf.InsertMode = true;
        buf.SetCursorPosition(0, 2);
        buf.WriteChar('X');

        Assert.Equal('A', buf.GetCell(0, 0).Character);
        Assert.Equal('B', buf.GetCell(0, 1).Character);
        Assert.Equal('X', buf.GetCell(0, 2).Character);
        Assert.Equal('C', buf.GetCell(0, 3).Character);
        Assert.Equal('D', buf.GetCell(0, 4).Character);
        Assert.Equal('E', buf.GetCell(0, 5).Character);
    }

    // ── Origin Mode ──────────────────────────────────────────────────────

    [Fact]
    public void OriginMode_CursorPositionRelativeToScrollRegion()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetScrollRegion(2, 7); // rows 2-7
        buf.OriginMode = true;
        buf.SetCursorPosition(0, 0); // Should map to row 2

        Assert.Equal(2, buf.CursorRow);
        Assert.Equal(0, buf.CursorCol);
    }

    [Fact]
    public void OriginMode_CursorClampedToScrollRegion()
    {
        var buf = CreateBuffer(10, 20);
        buf.SetScrollRegion(2, 7); // rows 2-7
        buf.OriginMode = true;
        buf.SetCursorPosition(10, 0); // Should clamp to row 7

        Assert.Equal(7, buf.CursorRow);
    }

    // ── Custom Tab Stops ─────────────────────────────────────────────────

    [Fact]
    public void SetTabStop_CreatesCustomStop()
    {
        var buf = CreateBuffer(5, 80);
        buf.SetCursorPosition(0, 5);
        buf.SetTabStop();
        buf.SetCursorPosition(0, 0);
        buf.Tab();

        Assert.Equal(5, buf.CursorCol);
    }

    [Fact]
    public void ClearAllTabStops_FallsBackToDefault()
    {
        var buf = CreateBuffer(5, 80);
        buf.SetTabStop(); // Set at col 0
        buf.ClearAllTabStops();
        buf.SetCursorPosition(0, 1);
        buf.Tab();

        Assert.Equal(8, buf.CursorCol); // Default 8-column stop
    }

    [Fact]
    public void ClearTabStop_RemovesSpecificStop()
    {
        var buf = CreateBuffer(5, 80);
        buf.SetCursorPosition(0, 5);
        buf.SetTabStop();
        buf.ClearTabStop(); // Clear at col 5
        buf.SetCursorPosition(0, 1);
        buf.Tab();

        // Should go to next default stop (8) since 5 was cleared
        Assert.Equal(8, buf.CursorCol);
    }

    // ── REP — Repeat Last Character ──────────────────────────────────────

    [Fact]
    public void RepeatLastChar_RepeatsNTimes()
    {
        var buf = CreateBuffer(5, 20);
        buf.WriteChar('X');
        buf.RepeatLastChar(3);

        Assert.Equal('X', buf.GetCell(0, 0).Character);
        Assert.Equal('X', buf.GetCell(0, 1).Character);
        Assert.Equal('X', buf.GetCell(0, 2).Character);
        Assert.Equal('X', buf.GetCell(0, 3).Character);
        Assert.Equal(4, buf.CursorCol);
    }

    [Fact]
    public void RepeatLastChar_NoOpWhenNoCharWritten()
    {
        var buf = CreateBuffer(5, 20);
        buf.RepeatLastChar(5);

        Assert.Equal(0, buf.CursorCol);
    }

    // ── DECALN — Fill With E ─────────────────────────────────────────────

    [Fact]
    public void FillWithE_FillsEntireScreen()
    {
        var buf = CreateBuffer(3, 5);
        buf.FillWithE();

        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 5; c++)
                Assert.Equal('E', buf.GetCell(r, c).Character);

        Assert.Equal(0, buf.CursorRow);
        Assert.Equal(0, buf.CursorCol);
    }

    // ── Soft Reset ───────────────────────────────────────────────────────

    [Fact]
    public void SoftReset_ResetsModesButKeepsContent()
    {
        var buf = CreateBuffer(5, 10);
        buf.WriteChar('X');
        buf.AutoWrap = false;
        buf.OriginMode = true;
        buf.InsertMode = true;
        buf.ApplicationCursorKeys = true;

        buf.SoftReset();

        // Content preserved
        Assert.Equal('X', buf.GetCell(0, 0).Character);
        // Modes reset
        Assert.True(buf.AutoWrap);
        Assert.False(buf.OriginMode);
        Assert.False(buf.InsertMode);
        Assert.False(buf.ApplicationCursorKeys);
        Assert.True(buf.CursorVisible);
    }

    // ── Save/Restore DEC Modes ───────────────────────────────────────────

    [Fact]
    public void SaveRestoreDecMode_Works()
    {
        var buf = CreateBuffer(5, 10);
        buf.ApplicationCursorKeys = true;
        buf.SaveDecMode(1, buf.GetDecMode(1));

        buf.ApplicationCursorKeys = false;
        var saved = buf.RestoreDecMode(1);

        Assert.True(saved.HasValue);
        Assert.True(saved.Value);
    }

    [Fact]
    public void RestoreDecMode_ReturnsNullWhenNotSaved()
    {
        var buf = CreateBuffer(5, 10);
        Assert.Null(buf.RestoreDecMode(999));
    }

    // ── Blink and Hidden style ───────────────────────────────────────────

    [Fact]
    public void Style_BlinkAndHidden_Applied()
    {
        var buf = CreateBuffer(5, 10);
        buf.CurrentStyle.IsBlink = true;
        buf.CurrentStyle.IsHidden = true;
        buf.WriteChar('X');

        var cell = buf.GetCell(0, 0);
        Assert.True(cell.Style.IsBlink);
        Assert.True(cell.Style.IsHidden);
    }

    [Fact]
    public void Style_UnderlineColor_Applied()
    {
        var buf = CreateBuffer(5, 10);
        var brush = new SolidColorBrush(Colors.Blue);
        brush.Freeze();
        buf.CurrentStyle.IsUnderline = true;
        buf.CurrentStyle.UnderlineColor = brush;
        buf.WriteChar('X');

        var cell = buf.GetCell(0, 0);
        Assert.True(cell.Style.IsUnderline);
        Assert.Equal(brush, cell.Style.UnderlineColor);
    }

    // ── Mouse/Focus/Sync mode flags ──────────────────────────────────────

    [Fact]
    public void MouseMode_DefaultsToOff()
    {
        var buf = CreateBuffer(5, 10);
        Assert.Equal(0, buf.MouseMode);
        Assert.Equal(0, buf.MouseEncoding);
    }

    [Fact]
    public void FocusReporting_DefaultsToOff()
    {
        var buf = CreateBuffer(5, 10);
        Assert.False(buf.FocusReporting);
    }

    [Fact]
    public void CursorShape_DefaultsToZero()
    {
        var buf = CreateBuffer(5, 10);
        Assert.Equal(0, buf.CursorShape);
    }

    [Fact]
    public void FullReset_ClearsAllNewModes()
    {
        var buf = CreateBuffer(5, 10);
        buf.OriginMode = true;
        buf.InsertMode = true;
        buf.FocusReporting = true;
        buf.SynchronizedOutput = true;
        buf.MouseMode = 1000;
        buf.MouseEncoding = 1006;
        buf.CursorShape = 3;

        buf.FullReset();

        Assert.False(buf.OriginMode);
        Assert.False(buf.InsertMode);
        Assert.False(buf.FocusReporting);
        Assert.False(buf.SynchronizedOutput);
        Assert.Equal(0, buf.MouseMode);
        Assert.Equal(0, buf.MouseEncoding);
        Assert.Equal(0, buf.CursorShape);
    }

    // ── GetDecMode ───────────────────────────────────────────────────────

    [Fact]
    public void GetDecMode_ReturnsCorrectValues()
    {
        var buf = CreateBuffer(5, 10);
        buf.ApplicationCursorKeys = true;
        Assert.True(buf.GetDecMode(1));
        Assert.False(buf.GetDecMode(6)); // OriginMode
        buf.OriginMode = true;
        Assert.True(buf.GetDecMode(6));
    }

    // ── Alternate buffer for TUI apps (htop/vim/nano) ────────────────────

    [Fact]
    public void AltBuffer_PreservesFullRowContent()
    {
        // When htop/vim switch to alternate buffer, the full screen is used.
        // Ensure cells at the far right of each row are accessible.
        var buf = CreateBuffer(5, 10);
        buf.SwitchToAlternateBuffer();

        // Write to the last column of each row
        for (int r = 0; r < 5; r++)
        {
            buf.SetCursorPosition(r, 9);
            buf.WriteChar('X');
        }

        // Verify all far-right cells
        for (int r = 0; r < 5; r++)
            Assert.Equal('X', buf.GetCell(r, 9).Character);
    }

    [Fact]
    public void AltBuffer_StyledSpacesArePreserved()
    {
        // htop draws coloured bars with spaces that have background color set.
        // The renderer must include these "styled spaces" in the output.
        var buf = CreateBuffer(3, 10);
        buf.SwitchToAlternateBuffer();

        var brush = new SolidColorBrush(Colors.Blue);
        brush.Freeze();
        buf.CurrentStyle.Background = brush;

        // Write 10 spaces with blue background on row 0
        buf.SetCursorPosition(0, 0);
        for (int c = 0; c < 10; c++)
            buf.WriteChar(' ');

        // All cells should have the background style
        for (int c = 0; c < 10; c++)
        {
            var cell = buf.GetCell(0, c);
            Assert.Equal(' ', cell.Character);
            Assert.True(cell.Style.IsNonDefault, $"Cell (0,{c}) should have non-default style");
        }
    }

    [Fact]
    public void PrimaryBuffer_StyledSpacesExtendRenderTo()
    {
        // A space with a coloured background at the end of a line should
        // still be rendered (not trimmed by the lastNonSpace logic).
        var buf = CreateBuffer(3, 10);

        var brush = new SolidColorBrush(Colors.Red);
        brush.Freeze();
        buf.CurrentStyle.Background = brush;

        buf.SetCursorPosition(0, 5);
        buf.WriteChar(' ');
        buf.WriteChar(' ');

        // Cells 5-6 are spaces but have non-default style
        Assert.True(buf.GetCell(0, 5).Style.IsNonDefault);
        Assert.True(buf.GetCell(0, 6).Style.IsNonDefault);
    }

    // ── Scrollback cap ───────────────────────────────────────────────────

    [Fact]
    public void Scrollback_AccumulatesOnScrollUp()
    {
        var buf = CreateBuffer(3, 5);

        // Fill 3 rows and scroll once to push row 0 into scrollback
        for (int r = 0; r < 3; r++)
        {
            buf.WriteChar((char)('A' + r));
            if (r < 2) buf.LineFeed();
        }
        buf.LineFeed(); // Scroll: row 0 → scrollback

        Assert.Equal(1, buf.Scrollback.Count);
        Assert.Equal('A', buf.Scrollback[0][0].Character);
    }

    [Fact]
    public void Scrollback_StaysWithinMaxLimit()
    {
        var buf = CreateBuffer(2, 5);

        // Push many lines to exceed scrollback limit (5000)
        for (int i = 0; i < 5100; i++)
        {
            buf.WriteChar('X');
            buf.LineFeed();
            buf.CarriageReturn();
        }

        // Buffer stores up to 5000 scrollback lines
        Assert.True(buf.Scrollback.Count <= 5000);
    }

    // ── Scroll region for TUI layout ─────────────────────────────────────

    [Fact]
    public void ScrollRegion_ScrollUpStaysWithinRegion()
    {
        // htop uses scroll regions to update just the process list area.
        var buf = CreateBuffer(10, 20);
        buf.SetScrollRegion(2, 7); // Region rows 2-7

        // Position at bottom of region and scroll up
        buf.SetCursorPosition(7, 0);
        buf.WriteChar('Z');
        buf.LineFeed(); // Should scroll within region [2,7]

        // Row 2 should have been scrolled out; row 7 should be blank
        Assert.Equal(' ', buf.GetCell(7, 0).Character);
    }

    [Fact]
    public void ScrollRegion_ContentOutsideRegionUnchanged()
    {
        var buf = CreateBuffer(10, 20);

        // Write content at rows 0 and 9 (outside scroll region)
        buf.SetCursorPosition(0, 0);
        buf.WriteChar('T');
        buf.SetCursorPosition(9, 0);
        buf.WriteChar('B');

        buf.SetScrollRegion(2, 7);

        // Scroll within the region
        buf.SetCursorPosition(7, 0);
        for (int i = 0; i < 5; i++) buf.LineFeed();

        // Content outside region should be untouched
        Assert.Equal('T', buf.GetCell(0, 0).Character);
        Assert.Equal('B', buf.GetCell(9, 0).Character);
    }
}
