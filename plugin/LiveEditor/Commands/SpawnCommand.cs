using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using TheForest.Utils;
using UnityEngine;

namespace LiveEditor.Commands;

/// <summary>
/// item.spawn — places a world pickup. Strategy is chosen from the session role
/// (see SpawnService); "strategy" in the args forces a specific one.
/// </summary>
public sealed class ItemSpawnCommand : ICommandHandler
{
    // 0 means "no such layer" and is never a usable mask, so it must not be cached
    // as a success — doing so would permanently break "look" spawns after one bad
    // lookup (e.g. if queried before layers are registered).
    private static int _terrainMask;
    private static int TerrainMask
    {
        get
        {
            if (_terrainMask != 0) return _terrainMask;
            _terrainMask = LayerMask.GetMask(new[] { "Terrain" });
            return _terrainMask;
        }
    }

    public string Name => "item.spawn";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!GameStateTracker.DbReady)
            return CommandResult.Error(ErrorCodes.DbNotReady, "item database not initialized yet");
        if (!SessionState.InWorld)
            return CommandResult.Error(ErrorCodes.NotInWorld, "no active player/world");

        if (args is null || !args.Value.TryGetProperty("item", out var itemArg))
            return CommandResult.Error(ErrorCodes.BadArgs, "missing 'item'");
        if (!ItemLookup.TryResolve(itemArg, out var item))
            return CommandResult.Error(ErrorCodes.UnknownItem, $"no item matches '{itemArg}'");

        var qty = args.Value.TryGetProperty("qty", out var q) ? Math.Clamp(q.GetInt32(), 1, 100) : 1;
        var at = args.Value.TryGetProperty("at", out var a) ? (a.GetString() ?? "look") : "look";
        var maxDist = args.Value.TryGetProperty("maxDist", out var d) ? d.GetSingle() : 30f;

        var strategy = SpawnService.ChooseStrategy();
        if (args.Value.TryGetProperty("strategy", out var sArg))
        {
            strategy = (sArg.GetString() ?? "").ToLowerInvariant() switch
            {
                "direct" => SpawnStrategy.Direct,
                "bolt" => SpawnStrategy.Bolt,
                "console" => SpawnStrategy.Console,
                _ => strategy,
            };
        }

        // Console strategy ignores position entirely, so don't fail on a missed raycast.
        var havePos = false;
        var pos = Vector3.zero;
        if (strategy != SpawnStrategy.Console)
        {
            if (!TryGetBasePosition(at, maxDist, out pos, out var posErr))
                return CommandResult.Error(ErrorCodes.BadArgs, posErr);
            havePos = true;
        }

        bool? plated = args.Value.TryGetProperty("plated", out var pl) && pl.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? pl.GetBoolean()
            : null;
        if (plated == true && !ItemModules.CanBePlated(item))
            return CommandResult.Error(ErrorCodes.Unsupported,
                $"'{ItemNames.DisplayName(item)}' cannot be plated (ItemData._canBePlated is false)");

        var outcome = SpawnService.Spawn(item, qty, strategy, pos, havePos, plated);
        if (!outcome.Ok)
            return CommandResult.Error(ErrorCodes.ExecFailed, outcome.Error ?? "spawn failed");

        var result = new JsonObject
        {
            ["spawned"] = outcome.Count,
            ["strategy"] = outcome.Strategy.ToString().ToLowerInvariant(),
            ["mode"] = SessionState.ModeString,
            ["item"] = new JsonObject { ["id"] = item.Id, ["name"] = ItemNames.DisplayName(item) },
        };
        if (havePos && outcome.Strategy != SpawnStrategy.Console)
            result["pos"] = new JsonArray(pos.x, pos.y, pos.z);
        if (!string.IsNullOrEmpty(outcome.Note)) result["note"] = outcome.Note;
        return CommandResult.Ok(result);
    }

    private static bool TryGetBasePosition(string at, float maxDist, out Vector3 pos, out string error)
    {
        error = "";
        if (at == "front")
        {
            try { pos = SonsSdk.SonsTools.GetPositionInFrontOfPlayer(2.5f); return true; }
            catch (Exception ex) { pos = Vector3.zero; error = ex.Message; return false; }
        }

        var camTr = LocalPlayer.MainCamTr;
        if (camTr == null) { pos = Vector3.zero; error = "no camera transform"; return false; }

        if (Physics.Raycast(camTr.position, camTr.forward, out var hit, maxDist, TerrainMask))
        {
            pos = hit.point;
            return true;
        }

        pos = Vector3.zero;
        error = $"no terrain within {maxDist}m of aim point";
        return false;
    }
}

/// <summary>spawn.unlock — clears the canBeSpawned gate so spawnitem accepts everything.</summary>
public sealed class SpawnUnlockCommand : ICommandHandler
{
    public string Name => "spawn.unlock";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!GameStateTracker.DbReady)
            return CommandResult.Error(ErrorCodes.DbNotReady, "item database not initialized yet");

        // Single item if 'item' given, otherwise the whole database.
        if (args is not null && args.Value.TryGetProperty("item", out var itemArg))
        {
            if (!ItemLookup.TryResolve(itemArg, out var one))
                return CommandResult.Error(ErrorCodes.UnknownItem, $"no item matches '{itemArg}'");
            var flipped = SpawnUnlock.Unlock(one);
            return CommandResult.Ok(new JsonObject
            {
                ["item"] = one.Id,
                ["changed"] = flipped,
                ["note"] = flipped ? $"spawnitem {one.Id} will now work" : "was already spawnable",
            });
        }

        var changed = SpawnUnlock.UnlockAll();
        return CommandResult.Ok(new JsonObject
        {
            ["unlocked"] = changed,
            ["totalTracked"] = SpawnUnlock.UnlockedCount,
            ["note"] = "every item is now accepted by spawnitem, including from the in-game console. Runtime only - never written to your save, and reset by a game restart.",
        });
    }
}

/// <summary>spawn.restore — puts every canBeSpawned flag back.</summary>
public sealed class SpawnRestoreCommand : ICommandHandler
{
    public string Name => "spawn.restore";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        var n = SpawnUnlock.RestoreAll();
        return CommandResult.Ok(new JsonObject { ["restored"] = n });
    }
}

/// <summary>recon.prefab — reports whether an item's pickup prefab is network-spawnable.</summary>
public sealed class ReconPrefabCommand : ICommandHandler
{
    public string Name => "recon.prefab";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!GameStateTracker.DbReady)
            return CommandResult.Error(ErrorCodes.DbNotReady, "item database not initialized yet");
        if (args is null || !args.Value.TryGetProperty("item", out var itemArg))
            return CommandResult.Error(ErrorCodes.BadArgs, "missing 'item'");
        if (!ItemLookup.TryResolve(itemArg, out var item))
            return CommandResult.Error(ErrorCodes.UnknownItem, $"no item matches '{itemArg}'");

        var prefabTr = item.PickupPrefab;
        if (prefabTr == null)
            return CommandResult.Ok(new JsonObject { ["hasPickupPrefab"] = false });

        var go = prefabTr.gameObject;
        var boltRegistered = SpawnService.IsBoltRegistered(go, out var prefabId);

        var components = new JsonArray();
        try
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null) components.Add(c.GetIl2CppType().Name);
            }
        }
        catch { /* best effort */ }

        return CommandResult.Ok(new JsonObject
        {
            ["item"] = new JsonObject { ["id"] = item.Id, ["name"] = ItemNames.DisplayName(item) },
            ["hasPickupPrefab"] = true,
            ["prefabName"] = go.name,
            ["canBeSpawnedFlag"] = item._canBeSpawned,
            ["boltRegistered"] = boltRegistered,
            ["boltPrefabId"] = prefabId,
            ["components"] = components,
            ["sessionMode"] = SessionState.ModeString,
            ["chosenStrategy"] = SpawnService.ChooseStrategy().ToString().ToLowerInvariant(),
        });
    }
}
