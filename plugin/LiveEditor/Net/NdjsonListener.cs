using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using LiveEditor.Core;

namespace LiveEditor.Net;

/// <summary>
/// Loopback-only NDJSON TCP listener (PLAN.md 4.1/4.2). Socket threads only ever
/// touch this class and MainThreadDispatcher.Enqueue — never game APIs directly.
/// </summary>
public sealed class NdjsonListener
{
    private readonly CommandRegistry _registry;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly string _token;
    private readonly List<ChannelWriter<string>> _sessions = new();
    private readonly List<TcpClient> _clients = new();
    private readonly object _sessionsLock = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port { get; private set; }

    public NdjsonListener(CommandRegistry registry, MainThreadDispatcher dispatcher, string token)
    {
        _registry = registry;
        _dispatcher = dispatcher;
        _token = token;
    }

    /// <summary>Binds starting at preferredPort, incrementing on conflict (3.5 failure-mode table).</summary>
    public void Start(int preferredPort, int maxPortAttempts = 10)
    {
        _cts = new CancellationTokenSource();
        var port = preferredPort;
        for (var attempt = 0; attempt < maxPortAttempts; attempt++, port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                Port = port;
                break;
            }
            catch (SocketException)
            {
                _listener = null;
            }
        }

        if (_listener == null)
            throw new InvalidOperationException(
                $"Could not bind any port in range {preferredPort}-{port - 1}");

        _ = AcceptLoop(_cts.Token);
    }

    /// <summary>
    /// Closes the listener AND every accepted client. Stopping the listener alone is
    /// not enough: StreamReader.ReadLineAsync is not cancellable on net6.0, so a
    /// connected session would sit blocked on a read forever and keep its threads
    /// alive through domain teardown. Closing the socket is what unblocks it.
    /// </summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { /* already stopped */ }

        List<TcpClient> clients;
        lock (_sessionsLock)
        {
            clients = new List<TcpClient>(_clients);
            _clients.Clear();
            _sessions.Clear();
        }
        foreach (var c in clients)
        {
            try { c.Close(); } catch { }
        }

        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    /// <summary>Fire-and-forget push to every connected, authenticated session.</summary>
    public void Broadcast(string eventName, JsonNode? data = null)
    {
        var line = Envelope.BuildPush(eventName, data);
        lock (_sessionsLock)
        {
            foreach (var w in _sessions) w.TryWrite(line);
        }
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync();
                _ = HandleClient(client, ct);
            }
        }
        catch (Exception)
        {
            // listener stopped/disposed — normal on shutdown
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        var clientId = Guid.NewGuid().ToString("N")[..8];
        using var _ = client;
        client.NoDelay = true;
        lock (_sessionsLock) _clients.Add(client);

        var stream = client.GetStream();
        var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        var outbound = Channel.CreateUnbounded<string>();

        var writerTask = WriteLoop(stream, outbound.Reader);

        try
        {
            var firstLine = await ReadLineWithTimeout(reader, TimeSpan.FromSeconds(5));
            if (firstLine == null) return; // no hello within 5s, or disconnected

            var authed = await ProcessHello(firstLine, outbound.Writer);
            if (!authed) return;

            lock (_sessionsLock) _sessions.Add(outbound.Writer);
            try
            {
                // Pipeline: the read loop enqueues a dispatcher job per line and
                // moves straight to the next line without waiting for the main
                // thread to actually run it. This lets a burst of commands from one
                // connection batch into a single Update() tick's frame budget
                // instead of costing one full round-trip (i.e. one rendered frame)
                // per command — verified needed empirically: without this, 1000
                // pings took ~16.7s (one per title-screen frame at 60fps).
                var pending = Channel.CreateUnbounded<Task<string>>();
                var pumpTask = PumpResponses(pending.Reader, outbound.Writer);

                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break; // client disconnected
                    // ProcessLine runs synchronously up to its first await, which
                    // is the dispatcher enqueue itself — so the job is already in
                    // the queue by the time we loop back to read the next line.
                    var responseTask = ProcessLine(line, clientId);
                    await pending.Writer.WriteAsync(responseTask);
                }

                pending.Writer.Complete();
                await pumpTask;
            }
            finally
            {
                lock (_sessionsLock) _sessions.Remove(outbound.Writer);
            }
        }
        catch (Exception)
        {
            // connection-level failure; drop silently, never let this reach the game
        }
        finally
        {
            lock (_sessionsLock) _clients.Remove(client);
            outbound.Writer.TryComplete();
            try { await writerTask; } catch { /* ignore */ }
        }
    }

    private static async Task<string?> ReadLineWithTimeout(StreamReader reader, TimeSpan timeout)
    {
        // .NET 6's StreamReader has no cancellable ReadLineAsync overload (added in
        // .NET 7); net6.0 is fixed by the loader's runtime, so timeout via WhenAny.
        var readTask = reader.ReadLineAsync();
        var winner = await Task.WhenAny(readTask, Task.Delay(timeout));
        return winner == readTask ? await readTask : null;
    }

    private async Task<bool> ProcessHello(string line, ChannelWriter<string> outbound)
    {
        var req = Envelope.TryParseRequest(line);
        if (req is null || req.Value.Cmd != "hello")
        {
            await outbound.WriteAsync(Envelope.BuildErrorResponse(
                req?.Id ?? 0, ErrorCodes.Protocol, "first message must be 'hello'"));
            return false;
        }

        // Guard on ValueKind: an untyped GetInt32/GetString on client-supplied JSON
        // throws, which the catch-all swallows and closes the socket with no error
        // frame — making a client type error look identical to an auth rejection.
        var args = req.Value.Args;
        var protocol = args?.TryGetProperty("protocol", out var p) == true && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32()
            : 0;
        var token = args?.TryGetProperty("token", out var t) == true && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        if (protocol != 1)
        {
            await outbound.WriteAsync(Envelope.BuildErrorResponse(
                req.Value.Id, ErrorCodes.Protocol, "unsupported protocol version",
                new JsonObject { ["supported"] = 1 }));
            return false;
        }

        if (token is null || !PortFile.FixedTimeEquals(token, _token))
        {
            await outbound.WriteAsync(Envelope.BuildErrorResponse(
                req.Value.Id, ErrorCodes.Auth, "invalid token"));
            return false;
        }

        var result = new JsonObject
        {
            ["protocol"] = 1,
            ["plugin"] = "0.1.0",
            ["loader"] = "RedLoader 0.8.6",
            ["degraded"] = false,
        };
        await outbound.WriteAsync(Envelope.BuildOkResponse(req.Value.Id, result));
        return true;
    }

    /// <summary>
    /// Builds the response line for one request. Runs synchronously up to the
    /// dispatcher enqueue (see the pipelining note in HandleClient) — callers must
    /// NOT await this inline while reading the next line off the same connection.
    /// </summary>
    private async Task<string> ProcessLine(string line, string clientId)
    {
        var req = Envelope.TryParseRequest(line);
        if (req is null)
        {
            // Salvage the id if at all possible: replying with id 0 gives the client
            // nothing to correlate, so it waits out its full timeout instead of
            // seeing the protocol error immediately.
            return Envelope.BuildErrorResponse(Envelope.TrySalvageId(line), ErrorCodes.Protocol, "malformed request");
        }

        if (!_registry.TryGet(req.Value.Cmd, out var handler))
        {
            return Envelope.BuildErrorResponse(
                req.Value.Id, ErrorCodes.UnknownCmd, $"unknown command '{req.Value.Cmd}'");
        }

        var ctx = new CommandContext(clientId);

        // No WaitAsync here: the dispatcher owns the deadline, so a job that expires
        // in the queue is reported AND skipped rather than run later behind our back.
        var result = await _dispatcher.Enqueue(() => handler.Execute(req.Value.Args, ctx));

        // Serialization can throw on values that construct fine (non-finite floats,
        // for one). Keep that failure attached to the right request id instead of
        // letting it escape and take the connection's response pump with it.
        try
        {
            return result switch
            {
                CommandOk ok => Envelope.BuildOkResponse(req.Value.Id, ok.Result),
                CommandError err => Envelope.BuildErrorResponse(req.Value.Id, err.Code, err.Message, err.Data),
                _ => Envelope.BuildErrorResponse(req.Value.Id, ErrorCodes.ExecFailed, "internal: unhandled result"),
            };
        }
        catch (Exception ex)
        {
            return Envelope.BuildErrorResponse(req.Value.Id, ErrorCodes.ExecFailed,
                $"result could not be serialized: {ex.Message}");
        }
    }

    /// <summary>
    /// Awaits each response Task strictly in enqueue order and writes it out.
    ///
    /// Must never fault. A bare `await responseTask` here used to unwind the whole
    /// pump on a single bad response (e.g. a NaN stat value, which builds fine but
    /// throws at JSON serialization time). The read loop kept accepting commands and
    /// kept executing them against the game, while every reply was silently dropped —
    /// a wedged connection that still mutated world state.
    /// </summary>
    private static async Task PumpResponses(ChannelReader<Task<string>> pending, ChannelWriter<string> outbound)
    {
        await foreach (var responseTask in pending.ReadAllAsync())
        {
            string line;
            try
            {
                line = await responseTask;
            }
            catch (Exception ex)
            {
                line = Envelope.BuildErrorResponse(0, ErrorCodes.ExecFailed,
                    $"failed to build response: {ex.Message}");
            }

            try
            {
                await outbound.WriteAsync(line);
            }
            catch (Exception)
            {
                return; // outbound closed; the read side will tear the session down
            }
        }
    }

    private static async Task WriteLoop(NetworkStream stream, ChannelReader<string> outbound)
    {
        var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false, NewLine = "\n" };
        try
        {
            await foreach (var line in outbound.ReadAllAsync())
            {
                await writer.WriteLineAsync(line);
                await writer.FlushAsync();
            }
        }
        catch (Exception)
        {
            // client gone / stream broken — the read side will notice and clean up
        }
    }
}
