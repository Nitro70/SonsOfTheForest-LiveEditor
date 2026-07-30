using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using TheForest.Utils;

namespace LiveEditor.Commands;

public sealed class PingCommand : ICommandHandler
{
    public string Name => "ping";

    public CommandResult Execute(JsonElement? args, CommandContext ctx) =>
        CommandResult.Ok(new JsonObject { ["t"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
}

public sealed class StateGetCommand : ICommandHandler
{
    public string Name => "state.get";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        // SessionState.InWorld rather than LocalPlayer.IsInWorld: the latter reports
        // false while the inventory is open, which is a state where every item
        // command is still perfectly valid.
        var inWorld = SessionState.InWorld;

        bool cheats;
        try { cheats = TheForest.DebugConsole.Instance?.IsConsoleAllowed() ?? false; }
        catch { cheats = false; }

        var result = new JsonObject
        {
            ["inWorld"] = inWorld,
            ["dbReady"] = GameStateTracker.DbReady,
            ["mode"] = SessionState.ModeString,
            ["cheatsEnabled"] = cheats,
        };
        return CommandResult.Ok(result);
    }
}

public sealed class DescribeCommand : ICommandHandler
{
    private readonly CommandRegistry _registry;

    public DescribeCommand(CommandRegistry registry) => _registry = registry;

    public string Name => "describe";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        var commands = new JsonArray();
        foreach (var handler in _registry.All)
        {
            commands.Add(new JsonObject
            {
                ["name"] = handler.Name,
                ["kind"] = "typed",
            });
        }

        return CommandResult.Ok(new JsonObject { ["commands"] = commands, ["protocol"] = 1 });
    }
}
