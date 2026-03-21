namespace Terminal.Ssh;

using Renci.SshNet;
using Renci.SshNet.Common;

public sealed class SshTerminalSession : ISshTerminalSession
{
    private SshClient? _client;
    private ShellStream? _stream;
    private CancellationTokenSource? _readCts;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? Disconnected;

    public bool IsConnected => _client?.IsConnected == true && _stream != null;

    public async Task ConnectAsync(string host, int port, string username,
        Func<string, CancellationToken, Task<string>> promptForPassword,
        int columns, int rows, CancellationToken ct = default)
    {
        // Show the password prompt in the terminal ourselves (matching real ssh behaviour),
        // then attempt auth with both PasswordAuthenticationMethod and
        // KeyboardInteractiveAuthenticationMethod so we work against any server config.
        // Retry with "Permission denied" on auth failure, just like openssh.
        string? deniedPrefix = null;

        while (true)
        {
            // Build prompt text: optionally prepend "Permission denied" on retries
            string promptText = (deniedPrefix != null ? deniedPrefix + "\r\n" : "")
                                + $"{username}@{host}'s password: ";
            string password = await promptForPassword(promptText, ct);

            // PasswordAuthenticationMethod covers most servers
            var pwdAuth = new PasswordAuthenticationMethod(username, password);

            // KeyboardInteractiveAuthenticationMethod covers PAM / challenge-response servers;
            // respond to every prompt with the same password (standard behaviour)
            var kbAuth = new KeyboardInteractiveAuthenticationMethod(username);
            kbAuth.AuthenticationPrompt += (_, args) =>
            {
                foreach (var p in args.Prompts)
                    p.Response = password;
            };

            var connectionInfo = new ConnectionInfo(host, port, username, pwdAuth, kbAuth);
            connectionInfo.Timeout = TimeSpan.FromSeconds(10);

            // Dispose any previous failed attempt
            try { _client?.Disconnect(); } catch { }
            try { _client?.Dispose(); } catch { }

            _client = new SshClient(connectionInfo);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                await Task.Run(() => _client.Connect(), linkedCts.Token);
                if (_client.IsConnected) break; // success → proceed to shell
                deniedPrefix = "Permission denied, please try again.";
            }
            catch (SshAuthenticationException)
            {
                deniedPrefix = "Permission denied, please try again.";
                // loop → ask again
            }
            // Non-auth exceptions (network error, timeout, …) propagate out
        }

        var termModes = new Dictionary<TerminalModes, uint>
        {
            [TerminalModes.ECHO] = 1,
            [TerminalModes.ICRNL] = 1,
            [TerminalModes.ONLCR] = 1,
        };

        _stream = _client!.CreateShellStream("xterm-256color", (uint)columns, (uint)rows, 0, 0, 65536, termModes);

        _readCts = new CancellationTokenSource();
        _ = ReadLoopAsync(_readCts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16384];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0)
                    break;
                var data = new byte[bytesRead];
                Array.Copy(buffer, data, bytesRead);
                DataReceived?.Invoke(this, data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                ErrorOccurred?.Invoke(this, ex.Message);
        }
        finally
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Send(byte[] data)
    {
        try
        {
            _stream?.Write(data, 0, data.Length);
            _stream?.Flush();
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { ErrorOccurred?.Invoke(this, ex.Message); }
    }

    public void Send(byte b) => Send(new[] { b });
    public void Send(string text) => Send(System.Text.Encoding.UTF8.GetBytes(text));

    public void Resize(int columns, int rows)
    {
        try
        {
            if (_stream == null) return;
            // Try ResizeTerminal method (SSH.NET 2025.1.0)
            var method = _stream.GetType().GetMethod("ResizeTerminal",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(_stream, new object[] { (uint)columns, (uint)rows, 0u, 0u });
                return;
            }
            // Try via internal _channel field
            var channelField = _stream.GetType().GetField("_channel",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (channelField?.GetValue(_stream) is { } channel)
            {
                var changeMethod = channel.GetType().GetMethod("SendWindowChangeRequest",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                changeMethod?.Invoke(channel, new object[] { (uint)columns, (uint)rows, 0u, 0u });
            }
        }
        catch { /* resize is best-effort */ }
    }

    public void SendCursorPositionReport(int row, int col) => Send($"\x1b[{row + 1};{col + 1}R");

    public void Disconnect()
    {
        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;
        try { _stream?.Dispose(); } catch { }
        try { _client?.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }
        _stream = null;
        _client = null;
    }

    public void Dispose() => Disconnect();
}
