using System.Text.Json.Nodes;

namespace LiveEditor.Core;

/// <summary>
/// Caches the serialized item catalogue.
///
/// Building it walks all ~308 ItemData entries and does a localization lookup per
/// item, which is a multi-hundred-millisecond main-thread stall — i.e. a visible
/// hitch every time the app refreshes its list. The frame budget in
/// MainThreadDispatcher does NOT prevent this: it limits how many jobs start per
/// frame, not how long one job runs.
///
/// The catalogue is static game asset data, so it only needs building once. The one
/// mutable field in the payload is canBeSpawned, which SpawnUnlock changes — hence
/// Invalidate() on that path rather than a blanket time-based expiry.
/// </summary>
public static class ItemCatalogCache
{
    private static string? _itemListJson;
    private static string? _dumpJson;

    public static void Invalidate()
    {
        _itemListJson = null;
        _dumpJson = null;
    }

    /// <summary>Cached item.list payload; <paramref name="build"/> runs only on a miss.</summary>
    public static JsonNode GetOrBuildList(Func<JsonNode> build)
    {
        if (_itemListJson != null)
        {
            var cached = JsonNode.Parse(_itemListJson);
            if (cached != null) return cached;
        }

        var fresh = build();
        _itemListJson = fresh.ToJsonString();
        // Re-parse so the caller never gets the same node instance we retain: JsonNode
        // has a single parent, and handing the identical object to two responses would
        // throw on the second attempt to attach it.
        return JsonNode.Parse(_itemListJson)!;
    }

    public static JsonNode GetOrBuildDump(Func<JsonNode> build)
    {
        if (_dumpJson != null)
        {
            var cached = JsonNode.Parse(_dumpJson);
            if (cached != null) return cached;
        }

        var fresh = build();
        _dumpJson = fresh.ToJsonString();
        return JsonNode.Parse(_dumpJson)!;
    }

    public static bool IsListCached => _itemListJson != null;
}
