namespace LiveEditor.Core;

/// <summary>
/// Passed to every handler's Execute call, which always runs on the main thread
/// (see MainThreadDispatcher). Session/world-state fields are filled in as later
/// phases add SessionState (PLAN.md 3.4); kept minimal for Phase 3.
/// </summary>
public sealed class CommandContext
{
    public string ClientId { get; }

    public CommandContext(string clientId)
    {
        ClientId = clientId;
    }
}
