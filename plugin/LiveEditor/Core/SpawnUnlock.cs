using Sons.Items.Core;

namespace LiveEditor.Core;

/// <summary>
/// Bypasses the game's "can this item be spawned?" gate.
///
/// spawnitem consults ItemData._canBeSpawned and refuses the 60 items where it is
/// false (artifact pieces, blueprints, story notes). That flag lives on a runtime
/// ScriptableObject: it is never written to a save and resets when the game restarts,
/// so flipping it is non-destructive. Once flipped, `spawnitem 662` works from the
/// real in-game console exactly like any other command — no special code path.
///
/// Originals are remembered so everything can be put back without a restart.
/// </summary>
public static class SpawnUnlock
{
    private static readonly Dictionary<int, bool> _originals = new();

    public static int UnlockedCount => _originals.Count;

    /// <summary>Unlock one item. Returns true if it was actually changed.</summary>
    public static bool Unlock(ItemData item)
    {
        if (item == null) return false;
        try
        {
            if (item._canBeSpawned) return false;
            // Reaching here means the flag is false, so that is what we remember.
            if (!_originals.ContainsKey(item.Id)) _originals[item.Id] = false;
            item._canBeSpawned = true;
            ItemCatalogCache.Invalidate(); // canBeSpawned is part of the cached payload
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Unlock every item in the database. Returns how many were changed.</summary>
    public static int UnlockAll()
    {
        var changed = 0;
        foreach (var item in ItemDatabaseManager.Items)
        {
            if (item == null) continue;
            if (Unlock(item)) changed++;
        }
        return changed;
    }

    /// <summary>Put every flipped flag back the way the game had it.</summary>
    public static int RestoreAll()
    {
        var restored = 0;
        foreach (var kv in _originals)
        {
            try
            {
                if (ItemDatabaseManager.TryFindItemById(kv.Key, out var item) && item != null)
                {
                    item._canBeSpawned = kv.Value;
                    restored++;
                }
            }
            catch { /* keep going */ }
        }
        _originals.Clear();
        ItemCatalogCache.Invalidate();
        return restored;
    }
}
