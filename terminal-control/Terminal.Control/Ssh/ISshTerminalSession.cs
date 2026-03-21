namespace Terminal.Ssh;

public interface ISshTerminalSession : IDisposable
{
    event EventHandler<byte[]>? DataReceived;
    event EventHandler<string>? ErrorOccurred;
    event EventHandler? Disconnected;

    bool IsConnected { get; }

    Task ConnectAsync(string host, int port, string username,
        Func<string, CancellationToken, Task<string>> promptForPassword,
        int columns, int rows, CancellationToken ct = default);
    void Disconnect();
    void Send(byte[] data);
    void Send(byte b);
    void Send(string text);
    void Resize(int columns, int rows);
    void SendCursorPositionReport(int row, int col);
}
