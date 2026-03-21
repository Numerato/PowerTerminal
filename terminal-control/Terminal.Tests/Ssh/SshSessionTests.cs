namespace Terminal.Tests.Ssh;

using Terminal.Ssh;
using Xunit;

public class SshSessionTests
{
    [Fact]
    public void InitiallyNotConnected()
    {
        var session = new SshTerminalSession();
        Assert.False(session.IsConnected);
    }

    [Fact]
    public void SendDoesNotThrowWhenNotConnected()
    {
        var session = new SshTerminalSession();
        var ex = Record.Exception(() => session.Send(new byte[] { 65 }));
        Assert.Null(ex);
    }

    [Fact]
    public void ResizeDoesNotThrowWhenNotConnected()
    {
        var session = new SshTerminalSession();
        var ex = Record.Exception(() => session.Resize(80, 24));
        Assert.Null(ex);
    }

    [Fact]
    public void DisconnectDoesNotThrowWhenNotConnected()
    {
        var session = new SshTerminalSession();
        var ex = Record.Exception(() => session.Disconnect());
        Assert.Null(ex);
    }

    [Fact]
    public void DisposeDoesNotThrowWhenNotConnected()
    {
        var session = new SshTerminalSession();
        var ex = Record.Exception(() => session.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public async Task ConnectToInvalidHostThrows()
    {
        var session = new SshTerminalSession();
        await Assert.ThrowsAnyAsync<Exception>(() =>
            session.ConnectAsync("invalid-host-xyz.local", 22, "user",
                (prompt, ct) => Task.FromResult("password"), 80, 24));
    }
}
