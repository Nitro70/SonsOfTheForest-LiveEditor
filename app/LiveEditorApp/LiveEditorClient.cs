using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiveEditorApp;

public sealed record CommandResponse(bool Ok, JsonNode? Result, string? ErrorCode, string? ErrorMessage)
{
    public string Describe() => Ok
        ? (Result?.ToJsonString() ?? "{}")
        : $"{ErrorCode}: {ErrorMessage}";
}

/// <summary>
/// NDJSON client for the in-game LiveEditor plugin. One JSON object per line over
/// loopback TCP; requests carry an id and responses are matched back to it, while
/// unsolicited push events arrive with no id.
/// </summary>
public sealed class LiveEditorClient : IDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<CommandResponse>> _pending = new();
    private TcpClient? _tcp;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private long _nextId;

    public bool IsConnected => _tcp?.Connected == true;

    public event Action<string>? OnLog;
    public event Action<string, JsonNode?>? OnPushEvent;
    public event Action<bool>? OnConnectionChanged;

    /// <summary>
    /// The plugin writes UserData\LiveEditor.port on startup: line 1 is the port,
    /// line 2 a per-launch token. Reading it beats hardcoding or port-scanning.
    /// </summary>
    public static (int Port, string Token)? ReadPortFile(string gameDir)
    {
        var path = Path.Combine(gameDir, "UserData", "LiveEditor.port");
        if (!File.Exists(path)) return null;
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return null;
        return int.TryParse(lines[0].Trim(), out var port) ? (port, lines[1].Trim()) : null;
    }

    public async Task<bool> ConnectAsync(string gameDir)
    {
        // Only signal a state change if we were actually connected: firing it
        // unconditionally here made every connect attempt flash "Disconnected" first.
        if (IsConnected) Disconnect();

        try
        {
            // Inside the try: ReadPortFile does file IO and can throw
            // (IOException/UnauthorizedAccess), which previously escaped straight out
            // of an async void UI handler and killed the app.
            var info = ReadPortFile(gameDir);
            if (info is null)
            {
                OnLog?.Invoke("LiveEditor.port not found - is the game running with the mod loaded?");
                return false;
            }

            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync("127.0.0.1", info.Value.Port);

            var stream = _tcp.GetStream();
            var utf8 = new UTF8Encoding(false); // never emit a BOM: it corrupts the first line
            _writer = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" };
            var reader = new StreamReader(stream, utf8);

            _cts = new CancellationTokenSource();
            _ = ReadLoop(reader, _cts.Token);

            var hello = await SendAsync("hello", new JsonObject
            {
                ["protocol"] = 1,
                ["token"] = info.Value.Token,
                ["client"] = "LiveEditorApp/1.0",
            });

            if (!hello.Ok)
            {
                OnLog?.Invoke($"handshake rejected: {hello.Describe()}");
                Disconnect();
                return false;
            }

            OnLog?.Invoke($"connected on port {info.Value.Port}");
            OnConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"connect failed: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        try { _cts?.Cancel(); } catch { }
        try { _tcp?.Close(); } catch { }
        _tcp = null;
        _writer = null;

        foreach (var kv in _pending)
            kv.Value.TrySetResult(new CommandResponse(false, null, "E_DISCONNECTED", "connection closed"));
        _pending.Clear();

        OnConnectionChanged?.Invoke(false);
    }

    public async Task<CommandResponse> SendAsync(string cmd, JsonNode? args = null, int timeoutMs = 15000)
    {
        var writer = _writer;
        if (writer is null)
            return new CommandResponse(false, null, "E_DISCONNECTED", "not connected");

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<CommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var envelope = new JsonObject { ["id"] = id, ["cmd"] = cmd };
        if (args is not null) envelope["args"] = args;

        try
        {
            await writer.WriteLineAsync(envelope.ToJsonString());
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            return new CommandResponse(false, null, "E_SEND_FAILED", ex.Message);
        }

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _pending.TryRemove(id, out _);
            return new CommandResponse(false, null, "E_TIMEOUT", $"no response within {timeoutMs}ms");
        }
    }

    private async Task ReadLoop(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line is null) break;
                Dispatch(line);
            }
        }
        catch (Exception) { /* socket closed */ }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                OnLog?.Invoke("connection lost");
                Disconnect();
            }
        }
    }

    private void Dispatch(string line)
    {
        try
        {
            var node = JsonNode.Parse(line);
            if (node is not JsonObject obj) return;

            // Push events carry an "event" name and no id.
            if (obj.TryGetPropertyValue("event", out var evt) && evt is not null)
            {
                OnPushEvent?.Invoke(evt.GetValue<string>(), obj["data"]);
                return;
            }

            if (!obj.TryGetPropertyValue("id", out var idNode) || idNode is null) return;
            var id = idNode.GetValue<long>();
            if (!_pending.TryRemove(id, out var tcs)) return;

            var ok = obj["ok"]?.GetValue<bool>() ?? false;
            if (ok)
            {
                tcs.TrySetResult(new CommandResponse(true, obj["result"], null, null));
            }
            else
            {
                var err = obj["error"];
                tcs.TrySetResult(new CommandResponse(false, null,
                    err?["code"]?.GetValue<string>() ?? "E_UNKNOWN",
                    err?["message"]?.GetValue<string>() ?? "(no message)"));
            }
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"malformed line: {ex.Message}");
        }
    }

    public void Dispose() => Disconnect();
}
