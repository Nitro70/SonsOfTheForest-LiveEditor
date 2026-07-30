using TheForest.Utils;

namespace LiveEditor.Core;

public enum SessionMode
{
    Unknown,
    SinglePlayer,
    MultiplayerHost,
    MultiplayerClient,
}

/// <summary>
/// Real session detection via Bolt (the game's networking layer), replacing the
/// placeholders state.get used to return. Used to pick a spawn strategy: direct
/// Instantiate is client-local and invisible to other players, so multiplayer
/// needs the game's own spawn path instead (see ItemSpawnCommand).
/// </summary>
public static class SessionState
{
    public static SessionMode Mode
    {
        get
        {
            try
            {
                if (!BoltNetwork.isRunning) return SessionMode.SinglePlayer;
                return BoltNetwork.isServer ? SessionMode.MultiplayerHost : SessionMode.MultiplayerClient;
            }
            catch
            {
                return SessionMode.Unknown;
            }
        }
    }

    public static bool IsMultiplayer
    {
        get
        {
            var m = Mode;
            return m == SessionMode.MultiplayerHost || m == SessionMode.MultiplayerClient;
        }
    }

    /// <summary>
    /// True whenever a live player exists, including while the inventory is open.
    ///
    /// LocalPlayer.IsInWorld alone is wrong for gating commands: it flips to false
    /// when the player opens the inventory (that view is a separate PlayerViews
    /// state), which made perfectly valid item edits fail with E_NOT_IN_WORLD while
    /// the user was standing in their own inventory. Fall back to the existence of
    /// the player object itself, which is the thing the commands actually need.
    /// </summary>
    public static bool InWorld
    {
        get
        {
            try
            {
                if (LocalPlayer.IsInWorld) return true;
                if (LocalPlayer.IsInInventory) return true;
                return LocalPlayer._instance != null && LocalPlayer.Inventory != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public static string ModeString => Mode switch
    {
        SessionMode.SinglePlayer => "sp",
        SessionMode.MultiplayerHost => "mp-host",
        SessionMode.MultiplayerClient => "mp-client",
        _ => "unknown",
    };
}
