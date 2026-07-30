using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using Sons.Crafting.Structures;

namespace LiveEditor.Commands;

/// <summary>
/// Commands that no longer exist in the console but whose machinery is still in the
/// shipped build. `buildhack` is the example: there is no _buildhack method any more
/// (verified by scanning every type/method/field in the game assemblies), but
/// StructureCraftingSystem still carries public InfiniteItemHack / InstantBuild
/// properties that the old command drove. We re-expose those directly.
///
/// This is deliberately a separate `restored.*` namespace so it is never confused
/// with a real console command in `describe` output.
/// </summary>
public sealed class RestoredBuildHackCommand : ICommandHandler
{
    public string Name => "restored.buildhack";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!TryGetCraftingSystem(out var sys, out var err))
            return CommandResult.Error(ErrorCodes.NotInWorld, err);

        // No 'enabled' arg -> report current state instead of toggling.
        if (args is null || !args.Value.TryGetProperty("enabled", out var en))
        {
            return CommandResult.Ok(new JsonObject
            {
                ["infiniteItems"] = sys.InfiniteItemHack,
                ["instantBuild"] = sys.InstantBuild,
            });
        }

        var enabled = en.GetBoolean();
        sys.InfiniteItemHack = enabled;

        // The old buildhack also implied instant completion; keep that opt-in.
        if (args.Value.TryGetProperty("instantBuild", out var ib))
            sys.InstantBuild = ib.GetBoolean();

        return CommandResult.Ok(new JsonObject
        {
            ["infiniteItems"] = sys.InfiniteItemHack,
            ["instantBuild"] = sys.InstantBuild,
        });
    }

    internal static bool TryGetCraftingSystem(out StructureCraftingSystem sys, out string error)
    {
        sys = null!;
        error = "";
        try
        {
            sys = StructureCraftingSystem.GetInstance();
            if (sys == null)
            {
                error = "StructureCraftingSystem has no instance yet (load into a world first)";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = $"could not reach StructureCraftingSystem: {ex.Message}";
            return false;
        }
    }
}

public sealed class RestoredInstantBuildCommand : ICommandHandler
{
    public string Name => "restored.instantbuild";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!RestoredBuildHackCommand.TryGetCraftingSystem(out var sys, out var err))
            return CommandResult.Error(ErrorCodes.NotInWorld, err);

        if (args is null || !args.Value.TryGetProperty("enabled", out var en))
            return CommandResult.Ok(new JsonObject { ["instantBuild"] = sys.InstantBuild });

        sys.InstantBuild = en.GetBoolean();
        return CommandResult.Ok(new JsonObject { ["instantBuild"] = sys.InstantBuild });
    }
}
