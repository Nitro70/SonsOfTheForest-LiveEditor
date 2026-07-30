using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using Sons.Items.Core;

namespace LiveEditor.Commands;

/// <summary>
/// Full item reference straight from the live ItemDatabaseManager. This is ground
/// truth: item definitions live in Unity assets, not in code, so the running game
/// is a more authoritative source than any decompile of GameAssembly.dll.
/// </summary>
public sealed class DumpItemsCommand : ICommandHandler
{
    public string Name => "dump.items";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!GameStateTracker.DbReady)
            return CommandResult.Error(ErrorCodes.DbNotReady, "item database not initialized");

        var arr = new JsonArray();
        foreach (var item in ItemDatabaseManager.Items)
        {
            if (item == null) continue;

            var tags = new JsonArray();
            try
            {
                var t = item._tags;
                if (t != null) for (var i = 0; i < t.Count; i++) tags.Add(t[i]);
            }
            catch { /* optional */ }

            var aliases = new JsonArray();
            try
            {
                var a = item._aliases;
                if (a != null) for (var i = 0; i < a.Count; i++) aliases.Add(a[i]);
            }
            catch { /* optional */ }

            arr.Add(new JsonObject
            {
                ["id"] = item.Id,
                ["displayName"] = ItemNames.DisplayName(item),
                ["internalName"] = item.Name,
                ["editorName"] = SafeStr(() => item._editorName),
                ["type"] = item.Type.ToString(),
                ["tags"] = tags,
                ["aliases"] = aliases,
                ["maxAmount"] = item.MaxAmount,
                ["canBeSpawned"] = SafeBool(() => item._canBeSpawned),
                ["canBePlated"] = SafeBool(() => item._canBePlated),
                ["eachInstanceIsUnique"] = SafeBool(() => item._eachInstanceIsUnique),
                ["isLegacyItem"] = SafeBool(() => item._isLegacyItem),
                ["canNotBeStored"] = SafeBool(() => item._canNotBeStored),
                ["maxWorldPickups"] = SafeInt(() => item._maxWorldPickups),
                ["hasPickupPrefab"] = SafeBool(() => item.PickupPrefab != null),
            });
        }

        return CommandResult.Ok(new JsonObject { ["count"] = arr.Count, ["items"] = arr });
    }

    private static string? SafeStr(Func<string> f) { try { return f(); } catch { return null; } }
    private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
    private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
}
