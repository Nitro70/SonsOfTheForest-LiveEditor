using HarmonyLib;
using Sons.Gameplay.GameSetup;
using TheForest;

namespace LiveEditor.Core;

/// <summary>
/// Forces this client's console gates open.
///
/// Three separate checks can shut the console down, and as a multiplayer client all
/// three are outside your control:
///   GameSetupManager.GetMultiplayerCheatsSetting() - the host's "Allow Cheats" switch,
///     which RedLoader reads and then calls SetCheatsAllowed with. If the host has it
///     off, RedLoader disables your console for you.
///   DebugConsole.IsConsoleAllowed() / IsConsoleBlocked() - the console's own gate,
///     consulted on the typed-input path.
///
/// SCOPE - this is a LOCAL bypass and cannot be anything else. It re-opens the console
/// on your own machine so client-side commands work. It does NOT grant server
/// authority: anything the host owns (world entity spawning, AI, world state) still
/// will not replicate from a client, no matter what these flags say. Bypassing a
/// permission check is not the same as gaining the permission.
///
/// Note the direct-invoke path in console.exec never consulted these gates in the
/// first place - it calls the command method itself rather than going through the
/// console's input handler. This mainly restores the in-game console UI and any
/// command that checks the flags internally.
/// </summary>
public static class CheatGateBypass
{
    /// <summary>On by default: the whole point is that a client cannot switch it on later.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Push the permissive state into the game's own fields as well as patching the getters.</summary>
    public static void Apply()
    {
        if (!Enabled) return;
        try { DebugConsole.SetCheatsAllowed(true); } catch { }
        try { DebugConsole.Instance?.SetBlockConsole(false); } catch { }
    }

    public static string DescribeState()
    {
        try
        {
            var c = DebugConsole.Instance;
            if (c == null) return "console not created yet";
            return $"allowed={c.IsConsoleAllowed()} blocked={c.IsConsoleBlocked()}";
        }
        catch (Exception ex)
        {
            return $"unreadable: {ex.Message}";
        }
    }
}

[HarmonyPatch(typeof(GameSetupManager), nameof(GameSetupManager.GetMultiplayerCheatsSetting))]
internal static class Patch_GetMultiplayerCheatsSetting
{
    private static bool Prefix(ref bool __result)
    {
        if (!CheatGateBypass.Enabled) return true; // run the real check
        __result = true;
        return false; // skip it
    }
}

[HarmonyPatch(typeof(DebugConsole), nameof(DebugConsole.IsConsoleAllowed))]
internal static class Patch_IsConsoleAllowed
{
    private static bool Prefix(ref bool __result)
    {
        if (!CheatGateBypass.Enabled) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(DebugConsole), nameof(DebugConsole.IsConsoleBlocked))]
internal static class Patch_IsConsoleBlocked
{
    private static bool Prefix(ref bool __result)
    {
        if (!CheatGateBypass.Enabled) return true;
        __result = false;
        return false;
    }
}
