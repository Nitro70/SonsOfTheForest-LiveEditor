using Sons.Items.Core;
using UnityEngine;

namespace LiveEditor.Core;

public enum SpawnStrategy
{
    /// <summary>UnityEngine Instantiate. Position-exact. NOT networked - single-player only.</summary>
    Direct,
    /// <summary>BoltNetwork.Instantiate. Position-exact AND replicated. Requires server authority.</summary>
    Bolt,
    /// <summary>The game's own _spawnitem console command. Position not controllable.</summary>
    Console,
}

public sealed record SpawnOutcome(bool Ok, int Count, SpawnStrategy Strategy, string? Error, string? Note);

/// <summary>
/// World pickups in this game are Bolt entities (PickUp carries _cachedBoltEntity and
/// PushItemInstanceToBolt), which is why a plain UnityEngine Instantiate produces an
/// object only the local player can see: the copy is never attached to the network.
/// BoltNetwork.Instantiate is the networked equivalent, but Photon Bolt gives entity
/// creation authority to the server, so a client cannot use it directly.
///
/// Strategy therefore depends on session role:
///   single-player -> Direct  (nothing to replicate to)
///   host          -> Bolt    (replicated and position-exact)
///   client        -> Console (hand it to the game and let its own logic decide)
/// </summary>
public static class SpawnService
{
    public static SpawnStrategy ChooseStrategy() => SessionState.Mode switch
    {
        SessionMode.MultiplayerHost => SpawnStrategy.Bolt,
        SessionMode.MultiplayerClient => SpawnStrategy.Console,
        _ => SpawnStrategy.Direct,
    };

    /// <summary>Is this prefab known to Bolt's prefab database (i.e. can it be network-spawned)?</summary>
    public static bool IsBoltRegistered(GameObject prefab, out int prefabId)
    {
        prefabId = 0;
        try
        {
            var entity = prefab.GetComponent<BoltEntity>();
            if (entity == null) return false;
            prefabId = entity._prefabId;
            return prefabId != 0;
        }
        catch
        {
            return false;
        }
    }

    public static SpawnOutcome Spawn(ItemData item, int qty, SpawnStrategy strategy, Vector3 pos, bool havePos,
        bool? plated = null)
    {
        // Every path below goes through the game's gate at some point, so clear it first.
        SpawnUnlock.Unlock(item);

        var outcome = strategy switch
        {
            SpawnStrategy.Console => SpawnConsole(item, qty),
            SpawnStrategy.Bolt => SpawnBolt(item, qty, pos, havePos),
            _ => SpawnDirect(item, qty, pos, havePos, plated),
        };

        // The console path hands off to the game and never gives us the resulting
        // object, so per-instance data cannot be attached there. Say so rather than
        // quietly ignoring the request.
        if (plated.HasValue && strategy != SpawnStrategy.Direct && outcome.Ok)
        {
            outcome = outcome with
            {
                Note = (outcome.Note is null ? "" : outcome.Note + " ") +
                       "NOTE: 'plated' was ignored - only the direct strategy returns the spawned object to attach item data to.",
            };
        }

        return outcome;
    }

    /// <summary>Attaches a fresh ItemInstance carrying the requested modules to a spawned pickup.</summary>
    private static void TryApplyInstance(UnityEngine.Object spawned, ItemData item, bool plated)
    {
        try
        {
            var go = spawned.TryCast<GameObject>();
            var pickup = go?.GetComponent<Sons.Gameplay.PickUp>();
            if (pickup == null) return;

            var instance = ItemModules.CreateInstance(item, plated, out _);
            if (instance != null) pickup.ItemInstance = instance;
        }
        catch
        {
            // pickup shape differs for this item; the object still spawned, just unplated
        }
    }

    private static SpawnOutcome SpawnConsole(ItemData item, int qty)
    {
        var console = TheForest.DebugConsole.Instance;
        if (console == null)
            return new SpawnOutcome(false, 0, SpawnStrategy.Console, "debug console not available (load a world first)", null);

        var idStr = item.Id.ToString();
        var issued = 0;
        try
        {
            for (var i = 0; i < qty; i++) { console._spawnitem(idStr); issued++; }
        }
        catch (Exception ex)
        {
            return new SpawnOutcome(false, issued, SpawnStrategy.Console, $"_spawnitem threw: {ex.Message}", null);
        }

        return new SpawnOutcome(true, issued, SpawnStrategy.Console, null,
            "spawned near the player through the game's own command; whether it replicates is the game's decision, not ours");
    }

    private static SpawnOutcome SpawnBolt(ItemData item, int qty, Vector3 pos, bool havePos)
    {
        var prefabTr = item.PickupPrefab;
        if (prefabTr == null)
            return new SpawnOutcome(false, 0, SpawnStrategy.Bolt, "item has no PickupPrefab", null);

        var prefabGo = prefabTr.gameObject;
        if (!IsBoltRegistered(prefabGo, out var prefabId))
        {
            // Not a networked prefab - fall back rather than silently spawning a ghost.
            var fallback = SpawnConsole(item, qty);
            return fallback with { Note = "pickup prefab is not Bolt-registered; used the console path instead" };
        }

        var spawned = 0;
        try
        {
            for (var i = 0; i < qty; i++)
            {
                var p = havePos ? pos + Vector3.up * 0.1f + Scatter(qty) : PlayerFront();
                var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                var entity = BoltNetwork.Instantiate(prefabGo, p, rot);
                if (entity != null) spawned++;
            }
        }
        catch (Exception ex)
        {
            return new SpawnOutcome(false, spawned, SpawnStrategy.Bolt,
                $"BoltNetwork.Instantiate threw: {ex.Message} (entity creation normally requires server authority)", null);
        }

        return new SpawnOutcome(true, spawned, SpawnStrategy.Bolt, null,
            $"network-spawned (bolt prefabId {prefabId}); other players should see these");
    }

    private static SpawnOutcome SpawnDirect(ItemData item, int qty, Vector3 pos, bool havePos, bool? plated = null)
    {
        var prefabTr = item.PickupPrefab;
        if (prefabTr == null)
            return new SpawnOutcome(false, 0, SpawnStrategy.Direct, "item has no PickupPrefab", null);

        var prefabObj = (UnityEngine.Object)prefabTr.gameObject;
        var spawned = 0;
        for (var i = 0; i < qty; i++)
        {
            var p = havePos ? pos + Vector3.up * 0.1f + Scatter(qty) : PlayerFront();
            var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            var obj = UnityEngine.Object.Instantiate(prefabObj, p, rot);
            spawned++;

            // Per-instance data (e.g. solafite plating) rides on the pickup's
            // ItemInstance, so it has to be attached to the spawned object rather
            // than applied to the prefab.
            if (plated.HasValue) TryApplyInstance(obj, item, plated.Value);
        }

        var note = SessionState.IsMultiplayer
            ? "WARNING: direct spawn is not networked - only you can see these"
            : null;
        return new SpawnOutcome(true, spawned, SpawnStrategy.Direct, null, note);
    }

    private static Vector3 Scatter(int qty) => qty > 1
        ? new Vector3(UnityEngine.Random.Range(-0.4f, 0.4f), 0f, UnityEngine.Random.Range(-0.4f, 0.4f))
        : Vector3.zero;

    private static Vector3 PlayerFront()
    {
        try { return SonsSdk.SonsTools.GetPositionInFrontOfPlayer(2.5f); }
        catch { return Vector3.zero; }
    }
}
