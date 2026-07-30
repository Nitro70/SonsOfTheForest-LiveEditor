namespace LiveEditor.Core;

/// <summary>
/// Tiny main-thread-only state cache. DbReady is set once after we force
/// ItemDatabaseManager.Initialize() at startup (PLAN.md 1.5 — lazy init, ~2s,
/// observed identically in the already-installed ItemSpawner mod's own log).
/// InWorld is cheap enough to query live off LocalPlayer.IsInWorld each time
/// instead of caching.
/// </summary>
public static class GameStateTracker
{
    public static bool DbReady { get; private set; }

    public static void MarkDbReady() => DbReady = true;
}
