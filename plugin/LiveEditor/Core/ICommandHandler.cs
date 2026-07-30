using System.Text.Json;

namespace LiveEditor.Core;

/// <summary>
/// Execute always runs on the Unity main thread (the dispatcher enqueues it) — safe
/// to call game APIs directly. Return promptly; long work should use a coroutine
/// registered separately, not block here (see PLAN.md 3.3 frame budget).
/// </summary>
public interface ICommandHandler
{
    string Name { get; }

    CommandResult Execute(JsonElement? args, CommandContext ctx);
}
