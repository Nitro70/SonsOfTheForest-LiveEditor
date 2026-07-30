using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using Sons.Inventory;
using Sons.Items.Core;
using TheForest.Utils;

namespace LiveEditor.Commands;

internal static class ItemLookup
{
    /// <summary>
    /// Resolves an "item" arg that may be a JSON number (id) or a string holding an
    /// id, an internal name ("ModernAxe"), or a friendly/display name ("Modern axe").
    /// Matching is normalized (case-insensitive, punctuation/space-insensitive) so
    /// callers never have to know the game's internal spelling.
    /// </summary>
    public static bool TryResolve(JsonElement itemArg, out ItemData itemData)
    {
        if (itemArg.ValueKind == JsonValueKind.Number && itemArg.TryGetInt32(out var idFromNumber))
            return ItemDatabaseManager.TryFindItemById(idFromNumber, out itemData);

        if (itemArg.ValueKind == JsonValueKind.String)
        {
            var s = (itemArg.GetString() ?? "").Trim();
            if (int.TryParse(s, out var idFromString))
                return ItemDatabaseManager.TryFindItemById(idFromString, out itemData);
            return TryResolveByName(s, out itemData);
        }

        itemData = null!;
        return false;
    }

    public static bool TryResolveByName(string query, out ItemData itemData)
    {
        itemData = null!;
        var needle = ItemNames.Normalize(query);
        if (needle.Length == 0) return false;

        ItemData? exact = null;
        ItemData? prefix = null;
        ItemData? contains = null;

        foreach (var item in ItemDatabaseManager.Items)
        {
            var internalNorm = ItemNames.Normalize(item.Name);
            var displayNorm = ItemNames.Normalize(ItemNames.DisplayName(item));

            if (internalNorm == needle || displayNorm == needle)
            {
                exact = item;
                break;
            }
            if (prefix == null && (internalNorm.StartsWith(needle) || displayNorm.StartsWith(needle)))
                prefix = item;
            else if (contains == null && (internalNorm.Contains(needle) || displayNorm.Contains(needle)))
                contains = item;
        }

        itemData = (exact ?? prefix ?? contains)!;
        return itemData != null;
    }

    public static JsonObject ToJson(ItemData item) => new()
    {
        ["id"] = item.Id,
        ["name"] = ItemNames.DisplayName(item),
        ["internalName"] = item.Name,
        ["type"] = item.Type.ToString(),
        ["maxAmount"] = item.MaxAmount,
        ["canBeSpawned"] = item._canBeSpawned,
        ["canBePlated"] = ItemModules.CanBePlated(item),
    };
}

/// <summary>
/// Finds the ItemInstances the player owns for one item id.
///
/// ItemInstanceManager only tracks items sitting in the inventory. Whatever is
/// currently in your hands (or worn) lives in an equipment slot on PlayerInventory —
/// RightHandItem / LeftHandItem / EquippedChestItem / EquippedFeetItem /
/// EquippedEyesItem — and is a distinct ItemInstance object. Looking only at the
/// manager therefore either misses "my current axe" entirely or silently edits a
/// different, stashed copy of it.
///
/// Both sources are merged here and de-duplicated by native pointer. When the caller
/// did not ask for every copy, the equipped one wins: "plate my axe" means the one
/// being held, not an arbitrary duplicate.
/// </summary>
internal static class OwnedInstances
{
    private static void AddIfMatch(List<ItemInstance> into, ItemInstance? candidate, int itemId)
    {
        if (candidate == null) return;
        try
        {
            if (candidate._itemID != itemId) return;
            foreach (var existing in into)
                if (existing.Pointer == candidate.Pointer) return;
            into.Add(candidate);
        }
        catch { /* a slot can hold a destroyed proxy; skip it */ }
    }

    /// <summary>Equipped/held copies, most-relevant first.</summary>
    private static List<ItemInstance> Equipped(int itemId)
    {
        var found = new List<ItemInstance>();
        var inv = LocalPlayer.Inventory;
        if (inv == null) return found;

        void Slot(Func<ItemInstance> get)
        {
            try { AddIfMatch(found, get(), itemId); } catch { }
        }

        Slot(() => inv.RightHandItem);
        Slot(() => inv.LeftHandItem);
        Slot(() => inv.EquippedChestItem);
        Slot(() => inv.EquippedFeetItem);
        Slot(() => inv.EquippedEyesItem);
        return found;
    }

    public static bool TryCollect(int itemId, bool all, out List<ItemInstance> targets,
        out int equippedCount, out string error)
    {
        targets = new List<ItemInstance>();
        equippedCount = 0;
        error = "";

        var equipped = Equipped(itemId);
        equippedCount = equipped.Count;

        var stored = new List<ItemInstance>();
        try
        {
            var manager = LocalPlayer.Inventory?._itemInstanceManager;
            if (manager != null)
            {
                var owned = manager.GetAllItemsOfType(itemId);
                if (owned != null)
                    for (var i = 0; i < owned.Count; i++)
                        AddIfMatch(stored, owned[i], itemId);
            }
        }
        catch (Exception ex)
        {
            error = $"inventory lookup failed: {ex.Message}";
            return false;
        }

        if (all)
        {
            targets.AddRange(equipped);
            foreach (var s in stored) AddIfMatch(targets, s, itemId);
            return true;
        }

        if (equipped.Count > 0) targets.Add(equipped[0]);
        else if (stored.Count > 0) targets.Add(stored[0]);
        return true;
    }
}

public sealed class ItemListCommand : ICommandHandler
{
    public string Name => "item.list";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!GameStateTracker.DbReady)
            return CommandResult.Error(ErrorCodes.DbNotReady, "item database not initialized yet");

        var filter = args?.TryGetProperty("filter", out var f) == true && f.ValueKind == JsonValueKind.String
            ? f.GetString()
            : null;

        // Build once and cache: the per-item localization lookup makes this an
        // expensive main-thread walk, and the catalogue is static asset data.
        var full = ItemCatalogCache.GetOrBuildList(() =>
        {
            var all = new JsonArray();
            foreach (var item in ItemDatabaseManager.Items)
            {
                if (item == null) continue;
                all.Add(ItemLookup.ToJson(item));
            }
            return new JsonObject { ["items"] = all };
        });

        if (string.IsNullOrEmpty(filter)) return CommandResult.Ok(full);

        // Filtering happens on the cached copy, so it costs nothing extra.
        var filtered = new JsonArray();
        if (full["items"] is JsonArray items)
        {
            foreach (var n in items)
            {
                var name = n?["name"]?.GetValue<string>() ?? "";
                var internalName = n?["internalName"]?.GetValue<string>() ?? "";
                if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    internalName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    // Re-parse to detach: a JsonNode has one parent, so adding the
                    // cached node itself would rip it out of the cached document.
                    // (DeepClone is .NET 8+, unavailable here.)
                    if (n != null) filtered.Add(JsonNode.Parse(n.ToJsonString()));
                }
            }
        }
        return CommandResult.Ok(new JsonObject { ["items"] = filtered });
    }
}

public sealed class ItemResolveCommand : ICommandHandler
{
    public string Name => "item.resolve";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!GameStateTracker.DbReady)
            return CommandResult.Error(ErrorCodes.DbNotReady, "item database not initialized yet");

        if (args is null || !args.Value.TryGetProperty("q", out var q))
            return CommandResult.Error(ErrorCodes.BadArgs, "missing 'q'");

        if (!ItemLookup.TryResolve(q, out var item))
            return CommandResult.Error(ErrorCodes.UnknownItem, $"no item matches '{q}'");

        return CommandResult.Ok(ItemLookup.ToJson(item));
    }
}

public sealed class ItemAddCommand : ICommandHandler
{
    public string Name => "item.add";

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

        var qty = args.Value.TryGetProperty("qty", out var q) && q.ValueKind == JsonValueKind.Number
            ? Math.Max(1, q.GetInt32())
            : 1;
        var preventAutoEquip = !args.Value.TryGetProperty("preventAutoEquip", out var pae)
                               || pae.ValueKind != JsonValueKind.False;

        // Optional per-instance data. Either the shorthand "plated": true, or a full
        // "modules": {...} spec — both build the item already configured rather than
        // modifying it afterwards.
        bool? plated = args.Value.TryGetProperty("plated", out var pl) && pl.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? pl.GetBoolean()
            : null;

        JsonElement? moduleSpec = args.Value.TryGetProperty("modules", out var ms) && ms.ValueKind == JsonValueKind.Object
            ? ms
            : null;

        if (plated == true && !ItemModules.CanBePlated(item))
            return CommandResult.Error(ErrorCodes.Unsupported,
                $"'{ItemNames.DisplayName(item)}' cannot be plated (ItemData._canBePlated is false)");

        var id = item.Id;
        var inv = LocalPlayer.Inventory;

        // Spike finding (PLAN.md 1.3): AddItem's amount param is unreliable for
        // cap-limited items — AddItem(log, 5) grants only 1, but looping single
        // adds reaches the real cap. So loop, verifying via AmountOf each step,
        // and stop the moment the count stops climbing or AddItem reports failure.
        var moduleApplied = new List<string>();
        var moduleSkipped = new List<string>();

        var startCount = inv.AmountOf(id, true, true);
        var newCount = startCount;
        var added = 0;
        var stopped = false;
        for (var i = 0; i < qty; i++)
        {
            var before = newCount;

            // A fresh instance per unit: modules are per-instance state, so reusing
            // one object across the loop would hand the game the same item twice.
            ItemInstance? instance = null;
            if (moduleSpec.HasValue)
            {
                instance = ItemModules.CreateInstance(item, moduleSpec, moduleApplied, moduleSkipped, out var specErr);
                if (instance == null)
                    return CommandResult.Error(ErrorCodes.ExecFailed, specErr);
            }
            else if (plated.HasValue)
            {
                instance = ItemModules.CreateInstance(item, plated, out var modErr);
                if (instance == null)
                    return CommandResult.Error(ErrorCodes.Unsupported, modErr);
            }

            var ok = inv.AddItem(id, 1, preventAutoEquip, false, instance);
            newCount = inv.AmountOf(id, true, true);
            if (newCount > before)
                added += newCount - before;
            if (!ok || newCount <= before)
            {
                stopped = true;
                break;
            }
        }

        // Distinguish the two ways an add can stop short (they look identical from
        // the game's bool return, but mean very different things to a caller):
        //   capped   - we granted some, then hit the item's ceiling
        //   refused  - the game rejected it outright, nothing was granted at all
        //              (e.g. equipment slot occupied, or a shoulder-carry item the
        //              player can't take right now)
        var capped = stopped && added > 0;
        var refused = stopped && added == 0;

        var result = new JsonObject
        {
            ["added"] = added,
            ["newCount"] = newCount,
            ["capped"] = capped,
            ["refused"] = refused,
            ["requested"] = qty,
        };
        if (plated.HasValue) result["plated"] = plated.Value;
        if (moduleApplied.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var s in moduleApplied.Distinct()) arr.Add(s);
            result["modulesApplied"] = arr;
        }
        if (moduleSkipped.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var s in moduleSkipped.Distinct()) arr.Add(s);
            result["modulesSkipped"] = arr;
        }
        return CommandResult.Ok(result);
    }
}

public sealed class ItemRemoveCommand : ICommandHandler
{
    public string Name => "item.remove";

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

        var qty = args.Value.TryGetProperty("qty", out var q) ? Math.Max(1, q.GetInt32()) : 1;
        var id = item.Id;
        var inv = LocalPlayer.Inventory;

        var newCount = inv.AmountOf(id, true, true);
        var removed = 0;
        for (var i = 0; i < qty; i++)
        {
            var before = newCount;
            if (before <= 0) break;
            inv.RemoveItem(id, 1, false, true, true, null, false);
            newCount = inv.AmountOf(id, true, true);
            if (newCount < before)
                removed += before - newCount;
            else
                break; // couldn't remove further
        }

        return CommandResult.Ok(new JsonObject { ["removed"] = removed, ["newCount"] = newCount });
    }
}

/// <summary>
/// item.plate {item, plated=true, all=false} — applies (or strips) solafite plating
/// on an item you already own, rather than on a freshly created one.
/// </summary>
public sealed class ItemPlateCommand : ICommandHandler
{
    public string Name => "item.plate";

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

        var plated = !args.Value.TryGetProperty("plated", out var pl) || pl.ValueKind != JsonValueKind.False;
        var all = args.Value.TryGetProperty("all", out var a) && a.ValueKind == JsonValueKind.True;
        var force = args.Value.TryGetProperty("force", out var f) && f.ValueKind == JsonValueKind.True;

        if (!ItemModules.CanBePlated(item) && !force)
            return CommandResult.Error(ErrorCodes.Unsupported,
                $"'{ItemNames.DisplayName(item)}' is not flagged _canBePlated — the solafite upgrader would refuse it too. Pass force:true to attempt it anyway (likely no visual, and possibly no damage bonus).");

        if (!OwnedInstances.TryCollect(item.Id, all, out var targets, out var equippedCount, out var lookupErr))
            return CommandResult.Error(ErrorCodes.ExecFailed, lookupErr);

        if (targets.Count == 0)
            return CommandResult.Error(ErrorCodes.BadArgs,
                $"you do not own '{ItemNames.DisplayName(item)}' — add it first, or use item.add with plated:true");

        var changed = 0;
        string lastError = "";
        foreach (var t in targets)
        {
            if (ItemModules.TrySetPlated(t, plated, out var err, force)) changed++;
            else lastError = err;
        }

        if (changed == 0)
            return CommandResult.Error(ErrorCodes.Unsupported,
                string.IsNullOrEmpty(lastError) ? "no instance could be plated" : lastError);

        var refreshed = ItemRefresh.Apply(targets, out var refreshNote);

        var result = new JsonObject
        {
            ["item"] = item.Id,
            ["name"] = ItemNames.DisplayName(item),
            ["plated"] = plated,
            ["instancesChanged"] = changed,
            ["equippedInstances"] = equippedCount,
            ["visualsRefreshed"] = refreshed,
            ["supportedByGame"] = ItemModules.CanBePlated(item),
        };
        if (!string.IsNullOrEmpty(refreshNote)) result["refreshNote"] = refreshNote;
        if (force && !ItemModules.CanBePlated(item))
            result["warning"] = "forced onto an item the game does not flag as platable: the module is attached but there is likely no plated material on this prefab, so expect no visual change.";
        return CommandResult.Ok(result);
    }
}

/// <summary>
/// item.modules — read or write per-instance item data on something you already own.
///   {item}                       -> report modules present and their values
///   {item, set:{...}, all:false} -> apply a module spec
/// Keys: plating, pauseDecay, arrowUpgradeActive, gpsActive, gpsPulse, gpsBeep,
///       limbFresh (booleans); ammoCount, ammoType, variant (ints); volume, fill (floats).
/// </summary>
public sealed class ItemModulesCommand : ICommandHandler
{
    public string Name => "item.modules";

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

        var all = args.Value.TryGetProperty("all", out var a) && a.ValueKind == JsonValueKind.True;

        if (!OwnedInstances.TryCollect(item.Id, all, out var targets, out var equippedCount, out var lookupErr))
            return CommandResult.Error(ErrorCodes.ExecFailed, lookupErr);

        if (targets.Count == 0)
            return CommandResult.Error(ErrorCodes.BadArgs,
                $"you do not own '{ItemNames.DisplayName(item)}' — add it first, or pass modules to item.add");

        // Read-only when no 'set' block is supplied.
        if (!args.Value.TryGetProperty("set", out var setSpec) || setSpec.ValueKind != JsonValueKind.Object)
        {
            return CommandResult.Ok(new JsonObject
            {
                ["item"] = item.Id,
                ["name"] = ItemNames.DisplayName(item),
                ["instances"] = targets.Count,
                ["equippedInstances"] = equippedCount,
                ["modules"] = ItemModules.Describe(targets[0]),
            });
        }

        var applied = new List<string>();
        var skipped = new List<string>();
        foreach (var t in targets) ItemModules.Apply(t, setSpec, applied, skipped);

        var appliedArr = new JsonArray();
        foreach (var s in applied.Distinct()) appliedArr.Add(s);
        var skippedArr = new JsonArray();
        foreach (var s in skipped.Distinct()) skippedArr.Add(s);

        // Only worth repainting if something actually landed.
        var refreshNote = "";
        var refreshed = applied.Count > 0 ? ItemRefresh.Apply(targets, out refreshNote) : 0;

        var result = new JsonObject
        {
            ["item"] = item.Id,
            ["name"] = ItemNames.DisplayName(item),
            ["instancesTouched"] = targets.Count,
            ["equippedInstances"] = equippedCount,
            ["visualsRefreshed"] = refreshed,
            ["applied"] = appliedArr,
            ["skipped"] = skippedArr,
            ["modules"] = ItemModules.Describe(targets[0]),
        };
        if (!string.IsNullOrEmpty(refreshNote)) result["refreshNote"] = refreshNote;
        return CommandResult.Ok(result);
    }
}

/// <summary>
/// Repaints live item objects after their per-instance data changed. Kept separate
/// so both item.plate and item.modules report refreshes the same way.
/// </summary>
internal static class ItemRefresh
{
    public static int Apply(List<ItemInstance> targets, out string note)
    {
        note = "";
        var refreshed = 0;
        string lastReason = "";

        foreach (var t in targets)
        {
            if (ItemModules.RefreshVisual(t, out var reason)) refreshed++;
            else lastReason = reason;
        }

        if (refreshed == 0 && targets.Count > 0)
            note = string.IsNullOrEmpty(lastReason)
                ? "no live item object to repaint"
                : lastReason;

        return refreshed;
    }
}

public sealed class ItemCountCommand : ICommandHandler
{
    public string Name => "item.count";

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

        var count = LocalPlayer.Inventory.AmountOf(item.Id, true, true);
        return CommandResult.Ok(new JsonObject { ["count"] = count, ["owned"] = count > 0 });
    }
}
