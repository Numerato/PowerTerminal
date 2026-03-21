namespace Terminal.Tests.Vt;

using Terminal.Vt;
using Xunit;
using System.Text;

public class TerminalEmulatorTests
{
    private static TerminalEmulator Create(int cols = 80, int rows = 24) => new(cols, rows);
    private static byte[] Enc(string s) => Encoding.UTF8.GetBytes(s);
    private static void Feed(TerminalEmulator e, string s)
    {
        var b = Enc(s);
        e.ProcessBytes(b, 0, b.Length);
    }

    [Fact]
    public void PrintsText()
    {
        var e = Create();
        Feed(e, "Hello");
        Assert.Equal(new Rune('H'), e.Buffer.GetCellCopy(0, 0).Character);
        Assert.Equal(new Rune('e'), e.Buffer.GetCellCopy(0, 1).Character);
        Assert.Equal(new Rune('o'), e.Buffer.GetCellCopy(0, 4).Character);
    }

    [Fact]
    public void CursorMovesRight()
    {
        var e = Create();
        Feed(e, "\x1b[5C");
        Assert.Equal(5, e.Buffer.CursorCol);
    }

    [Fact]
    public void CursorMovesUp()
    {
        var e = Create();
        Feed(e, "\x1b[10;1H"); // move to row 10
        Feed(e, "\x1b[3A");    // up 3
        Assert.Equal(6, e.Buffer.CursorRow); // 9 - 3 = 6
    }

    [Fact]
    public void CursorPosition()
    {
        var e = Create();
        Feed(e, "\x1b[5;10H");
        Assert.Equal(4, e.Buffer.CursorRow);
        Assert.Equal(9, e.Buffer.CursorCol);
    }

    [Fact]
    public void SgrBoldSetsAttribute()
    {
        var e = Create();
        Feed(e, "\x1b[1m");
        Assert.True(e.Buffer.CurrentAttributes.Bold);
        Feed(e, "\x1b[22m");
        Assert.False(e.Buffer.CurrentAttributes.Bold);
    }

    [Fact]
    public void SgrForegroundColor()
    {
        var e = Create();
        Feed(e, "\x1b[31m"); // red (index 1)
        Assert.Equal(ColorKind.Indexed, e.Buffer.CurrentAttributes.Foreground.Kind);
        Assert.Equal(1, e.Buffer.CurrentAttributes.Foreground.Index);
    }

    [Fact]
    public void SgrBrightForegroundColor()
    {
        var e = Create();
        Feed(e, "\x1b[91m"); // bright red (index 9)
        Assert.Equal(ColorKind.Indexed, e.Buffer.CurrentAttributes.Foreground.Kind);
        Assert.Equal(9, e.Buffer.CurrentAttributes.Foreground.Index);
    }

    [Fact]
    public void Sgr256ForegroundColor()
    {
        var e = Create();
        Feed(e, "\x1b[38;5;196m");
        Assert.Equal(ColorKind.Indexed, e.Buffer.CurrentAttributes.Foreground.Kind);
        Assert.Equal(196, e.Buffer.CurrentAttributes.Foreground.Index);
    }

    [Fact]
    public void SgrTrueColorForeground()
    {
        var e = Create();
        Feed(e, "\x1b[38;2;255;0;0m");
        Assert.Equal(ColorKind.TrueColor, e.Buffer.CurrentAttributes.Foreground.Kind);
        Assert.Equal(255, e.Buffer.CurrentAttributes.Foreground.R);
        Assert.Equal(0, e.Buffer.CurrentAttributes.Foreground.G);
        Assert.Equal(0, e.Buffer.CurrentAttributes.Foreground.B);
    }

    [Fact]
    public void SgrReset()
    {
        var e = Create();
        Feed(e, "\x1b[1;31m");
        Feed(e, "\x1b[0m");
        Assert.False(e.Buffer.CurrentAttributes.Bold);
        Assert.Equal(ColorKind.Default, e.Buffer.CurrentAttributes.Foreground.Kind);
    }

    [Fact]
    public void SgrReverseVideo()
    {
        var e = Create();
        Feed(e, "\x1b[7m");
        Assert.True(e.Buffer.CurrentAttributes.ReverseVideo);
        Feed(e, "\x1b[27m");
        Assert.False(e.Buffer.CurrentAttributes.ReverseVideo);
    }

    [Fact]
    public void ClearScreen()
    {
        var e = Create();
        Feed(e, "Hello");
        Feed(e, "\x1b[2J");
        Assert.Equal(new Rune(' '), e.Buffer.GetCellCopy(0, 0).Character);
    }

    [Fact]
    public void AltScreenSwitchClearsAndRestores()
    {
        var e = Create();
        Feed(e, "Hello");
        Feed(e, "\x1b[?1049h"); // switch to alt
        Assert.True(e.Buffer.UseAltBuffer);
        Assert.Equal(new Rune(' '), e.Buffer.GetCellCopy(0, 0).Character);
        Feed(e, "\x1b[?1049l"); // switch back
        Assert.False(e.Buffer.UseAltBuffer);
        Assert.Equal(new Rune('H'), e.Buffer.GetCellCopy(0, 0).Character);
    }

    [Fact]
    public void CursorVisibilityHide()
    {
        var e = Create();
        Feed(e, "\x1b[?25l");
        Assert.False(e.Buffer.CursorVisible);
    }

    [Fact]
    public void CursorVisibilityShow()
    {
        var e = Create();
        Feed(e, "\x1b[?25l");
        Feed(e, "\x1b[?25h");
        Assert.True(e.Buffer.CursorVisible);
    }

    [Fact]
    public void ApplicationCursorKeysMode()
    {
        var e = Create();
        Feed(e, "\x1b[?1h");
        Assert.True(e.ApplicationCursorKeys);
        Feed(e, "\x1b[?1l");
        Assert.False(e.ApplicationCursorKeys);
    }

    [Fact]
    public void BracketedPasteMode()
    {
        var e = Create();
        Feed(e, "\x1b[?2004h");
        Assert.True(e.BracketedPaste);
        Feed(e, "\x1b[?2004l");
        Assert.False(e.BracketedPaste);
    }

    [Fact]
    public void TestMouseMode()
    {
        var e = Create();
        Feed(e, "\x1b[?1000h");
        Assert.Equal(MouseMode.X10, e.MouseMode);
        Feed(e, "\x1b[?1000l");
        Assert.Equal(MouseMode.None, e.MouseMode);
    }

    [Fact]
    public void TitleChangedEventFires()
    {
        var e = Create();
        string? title = null;
        e.TitleChanged += (_, t) => title = t;
        Feed(e, "\x1b]0;MyTitle\x07");
        Assert.Equal("MyTitle", title);
    }

    [Fact]
    public void BellEventFires()
    {
        var e = Create();
        bool fired = false;
        e.BellRaised += (_, _) => fired = true;
        Feed(e, "\x07");
        Assert.True(fired);
    }

    [Fact]
    public void ScrollRegion()
    {
        var e = Create(10, 10);
        Feed(e, "\x1b[3;8r"); // scroll region rows 3-8 (1-based)
        Assert.Equal(2, e.Buffer.ScrollTop);  // 0-based
        Assert.Equal(7, e.Buffer.ScrollBottom); // 0-based
    }

    [Fact]
    public void ReverseIndex()
    {
        var e = Create(80, 24);
        Feed(e, "\x1b[5;1H"); // row 5 (1-based) = row 4 (0-based)
        Feed(e, "\x1bM");     // reverse index
        Assert.Equal(3, e.Buffer.CursorRow);
    }

    [Fact]
    public void FullReset()
    {
        var e = Create();
        Feed(e, "\x1b[1m\x1b[31m");
        Feed(e, "\u001bc"); // RIS
        Assert.False(e.Buffer.CurrentAttributes.Bold);
        Assert.Equal(ColorKind.Default, e.Buffer.CurrentAttributes.Foreground.Kind);
    }

    [Fact]
    public void InsertDeleteLines()
    {
        var e = Create(10, 10);
        Feed(e, "\x1b[1;1H"); // row 1
        Feed(e, "AAAAAAAAAA"); // fill row 0
        Feed(e, "\x1b[1;1H");
        Feed(e, "\x1b[1L"); // insert 1 line
        Assert.Equal(new Rune(' '), e.Buffer.GetCellCopy(0, 0).Character); // row 0 blank
        Assert.Equal(new Rune('A'), e.Buffer.GetCellCopy(1, 0).Character); // row 1 = old row 0
    }

    [Fact]
    public void LargeOutputProcessedCorrectly()
    {
        var e = Create(80, 24);
        var sb = new StringBuilder();
        for (int i = 0; i < 100; i++)
            sb.AppendLine($"Line {i,3}: " + new string('*', 60));
        var data = Enc(sb.ToString());
        e.ProcessBytes(data, 0, data.Length); // should not throw
        // Verify some content exists (exact position depends on scroll)
        bool hasContent = false;
        for (int r = 0; r < 24 && !hasContent; r++)
            for (int c = 0; c < 80 && !hasContent; c++)
                if (e.Buffer.GetCellCopy(r, c).Character.Value != ' ')
                    hasContent = true;
        Assert.True(hasContent);
    }
}
