namespace Terminal.Tests.Vt;

using Terminal.Vt;
using Xunit;
using System.Collections.Generic;
using System.Text;

class RecordingActions : IVtParserActions
{
    public record PrintAction(Rune R);
    public record ExecuteAction(byte C);
    public record CsiAction(byte Final, List<int> Params, List<byte> Intermediates, bool IsPrivate);
    public record EscAction(byte Final, List<byte> Intermediates);
    public record OscAction(string Command);

    public List<object> Actions = new();

    public void Print(Rune r) => Actions.Add(new PrintAction(r));
    public void Execute(byte c) => Actions.Add(new ExecuteAction(c));
    public void CsiDispatch(byte f, IReadOnlyList<int> p, IReadOnlyList<byte> i, bool priv)
        => Actions.Add(new CsiAction(f, new List<int>(p), new List<byte>(i), priv));
    public void EscDispatch(byte f, IReadOnlyList<byte> i)
        => Actions.Add(new EscAction(f, new List<byte>(i)));
    public void OscDispatch(string cmd) => Actions.Add(new OscAction(cmd));
    public void DcsHook(byte f, IReadOnlyList<int> p, IReadOnlyList<byte> i) { }
    public void DcsPut(byte c) { }
    public void DcsUnhook() { }
}

public class VtParserTests
{
    private (VtParser parser, RecordingActions rec) Create()
    {
        var rec = new RecordingActions();
        return (new VtParser(rec), rec);
    }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private static byte[] Raw(params byte[] bytes) => bytes;

    [Fact]
    public void PrintsAsciiText()
    {
        var (p, r) = Create();
        p.Feed(B("Hello"));
        Assert.Equal(5, r.Actions.Count);
        Assert.All(r.Actions, a => Assert.IsType<RecordingActions.PrintAction>(a));
        Assert.Equal(new Rune('H'), ((RecordingActions.PrintAction)r.Actions[0]).R);
        Assert.Equal(new Rune('o'), ((RecordingActions.PrintAction)r.Actions[4]).R);
    }

    [Fact]
    public void ExecutesBell()
    {
        var (p, r) = Create();
        p.Feed(Raw(0x07));
        Assert.Single(r.Actions);
        var exec = Assert.IsType<RecordingActions.ExecuteAction>(r.Actions[0]);
        Assert.Equal(0x07, exec.C);
    }

    [Fact]
    public void ExecutesCarriageReturn()
    {
        var (p, r) = Create();
        p.Feed(Raw(0x0D));
        Assert.Single(r.Actions);
        var exec = Assert.IsType<RecordingActions.ExecuteAction>(r.Actions[0]);
        Assert.Equal(0x0D, exec.C);
    }

    [Fact]
    public void ParsesSgrReset()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[m"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'m', csi.Final);
        Assert.False(csi.IsPrivate);
    }

    [Fact]
    public void ParsesSgrMultipleParams()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[1;32;42m"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'m', csi.Final);
        Assert.Equal(3, csi.Params.Count);
        Assert.Equal(1, csi.Params[0]);
        Assert.Equal(32, csi.Params[1]);
        Assert.Equal(42, csi.Params[2]);
    }

    [Fact]
    public void ParsesCursorUp()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[3A"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'A', csi.Final);
        Assert.Equal(3, csi.Params[0]);
    }

    [Fact]
    public void ParsesCursorPosition()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[10;20H"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'H', csi.Final);
        Assert.Equal(10, csi.Params[0]);
        Assert.Equal(20, csi.Params[1]);
    }

    [Fact]
    public void ParsesDECPrivateMode()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[?25h"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'h', csi.Final);
        Assert.True(csi.IsPrivate);
        Assert.Equal(25, csi.Params[0]);
    }

    [Fact]
    public void ParsesOscTitle()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b]0;MyTitle\x07"));
        Assert.Single(r.Actions);
        var osc = Assert.IsType<RecordingActions.OscAction>(r.Actions[0]);
        Assert.Equal("0;MyTitle", osc.Command);
    }

    [Fact]
    public void ParsesOscTitleWithST()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b]2;Title\x1b\\"));
        Assert.Single(r.Actions);
        var osc = Assert.IsType<RecordingActions.OscAction>(r.Actions[0]);
        Assert.Equal("2;Title", osc.Command);
    }

    [Fact]
    public void ParsesEscSaveCursor()
    {
        var (p, r) = Create();
        p.Feed(B("\u001b7"));
        Assert.Single(r.Actions);
        var esc = Assert.IsType<RecordingActions.EscAction>(r.Actions[0]);
        Assert.Equal((byte)'7', esc.Final);
    }

    [Fact]
    public void ParsesEscRestoreCursor()
    {
        var (p, r) = Create();
        p.Feed(B("\u001b8"));
        Assert.Single(r.Actions);
        var esc = Assert.IsType<RecordingActions.EscAction>(r.Actions[0]);
        Assert.Equal((byte)'8', esc.Final);
    }

    [Fact]
    public void ParsesEscReverseIndex()
    {
        var (p, r) = Create();
        p.Feed(B("\x1bM"));
        Assert.Single(r.Actions);
        var esc = Assert.IsType<RecordingActions.EscAction>(r.Actions[0]);
        Assert.Equal((byte)'M', esc.Final);
    }

    [Fact]
    public void HandlesDefaultParams()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[A")); // no param = default
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'A', csi.Final);
        // Default param = -1
        Assert.Empty(csi.Params); // no params provided, empty list
    }

    [Fact]
    public void HandlesMalformedSequence()
    {
        var (p, r) = Create();
        var ex = Record.Exception(() => p.Feed(B("\x1b[999999999999999m")));
        Assert.Null(ex);
    }

    [Fact]
    public void ParsesUtf8TwoByteChar()
    {
        var (p, r) = Create();
        p.Feed(new byte[] { 0xC3, 0xA9 }); // é
        Assert.Single(r.Actions);
        var print = Assert.IsType<RecordingActions.PrintAction>(r.Actions[0]);
        Assert.Equal(new Rune('é'), print.R);
    }

    [Fact]
    public void ParsesUtf8ThreeByteChar()
    {
        var (p, r) = Create();
        p.Feed(new byte[] { 0xE2, 0x9C, 0x93 }); // ✓
        Assert.Single(r.Actions);
        var print = Assert.IsType<RecordingActions.PrintAction>(r.Actions[0]);
        Assert.Equal(new Rune('✓'), print.R);
    }

    [Fact]
    public void ParsesCsiEraseDisplay()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[2J"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'J', csi.Final);
        Assert.Equal(2, csi.Params[0]);
    }

    [Fact]
    public void ParsesCsiDeleteChars()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[5P"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'P', csi.Final);
        Assert.Equal(5, csi.Params[0]);
    }

    [Fact]
    public void ParsesCsi256Color()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[38;5;196m"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'m', csi.Final);
        Assert.Equal(38, csi.Params[0]);
        Assert.Equal(5, csi.Params[1]);
        Assert.Equal(196, csi.Params[2]);
    }

    [Fact]
    public void ParsesCsiTrueColor()
    {
        var (p, r) = Create();
        p.Feed(B("\x1b[38;2;255;128;0m"));
        Assert.Single(r.Actions);
        var csi = Assert.IsType<RecordingActions.CsiAction>(r.Actions[0]);
        Assert.Equal((byte)'m', csi.Final);
        Assert.Equal(38, csi.Params[0]);
        Assert.Equal(2, csi.Params[1]);
        Assert.Equal(255, csi.Params[2]);
        Assert.Equal(128, csi.Params[3]);
        Assert.Equal(0, csi.Params[4]);
    }
}
