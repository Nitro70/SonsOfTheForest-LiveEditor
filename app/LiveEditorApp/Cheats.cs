namespace LiveEditorApp;

public enum CheatKind
{
    /// <summary>Sends "&lt;command&gt; on" / "&lt;command&gt; off" — the game's usual toggle form.</summary>
    OnOff,

    /// <summary>Argless command that flips state itself; the app can only track parity.</summary>
    Flip,

    /// <summary>Fires once and has no state. Rendered without an on/off tint.</summary>
    Action,

    /// <summary>Handled by a plugin command rather than the console, and reports real state back.</summary>
    Plugin,
}

/// <summary>
/// One button in Tools &amp; Cheats.
///
/// On the honesty of the on/off colour: the console gives no way to ask "is godmode
/// currently on?", so for OnOff and Flip entries the colour reflects what this app has
/// sent, not what the game believes. Toggling the same thing from the in-game console
/// will desync it. Plugin entries are exempt — those read their real state back.
///
/// Only commands whose on|off form is either verified or follows the game's documented
/// convention are listed here. Anything needing a numeric or name argument belongs on
/// the Console tab instead, where you can type the argument.
/// </summary>
public sealed class CheatDef
{
    public string Label = "";
    public string Command = "";
    public CheatKind Kind = CheatKind.OnOff;

    /// <summary>Group heading in the extended section. Core entries ignore this.</summary>
    public string Group = "";

    /// <summary>Core entries are always visible; the rest need "Show more commands".</summary>
    public bool Core;

    public string? Tip;

    public bool HasState => Kind != CheatKind.Action;

    public static readonly CheatDef[] All =
    {
        // ---- core: the ones people actually reach for ----------------------------
        new() { Label = "godmode",      Command = "godmode",    Core = true, Tip = "Invincibility. Verified: a felled tree dealt no damage with it on." },
        new() { Label = "invisible",    Command = "invisible",  Core = true, Tip = "Enemies stop seeing you." },
        new() { Label = "infinite stamina", Command = "energyhack", Core = true, Tip = "energyhack" },
        new() { Label = "infinite stone",   Command = "stonehack",  Core = true, Tip = "stonehack. Confirmed working." },
        new() { Label = "speedyrun",    Command = "speedyrun",  Core = true },
        new() { Label = "superjump",    Command = "superjump",  Core = true },
        new() { Label = "regenhealth",  Command = "regenhealth", Core = true },
        new() { Label = "no final death", Command = "blockplayerfinaldeath", Core = true, Tip = "blockplayerfinaldeath" },

        new() { Label = "infinite build items", Command = "restored.buildhack", Kind = CheatKind.Plugin, Core = true,
                Tip = "The removed buildhack, revived through StructureCraftingSystem.InfiniteItemHack. Reads its real state back." },
        new() { Label = "instantbuild", Command = "restored.instantbuild", Kind = CheatKind.Plugin, Core = true,
                Tip = "StructureCraftingSystem.InstantBuild. Reads its real state back." },

        new() { Label = "buffstats",    Command = "buffstats",  Kind = CheatKind.Action, Core = true, Tip = "One-shot stat buff." },
        new() { Label = "revive me",    Command = "revivelocalplayer", Kind = CheatKind.Action, Core = true, Tip = "revivelocalplayer" },

        // ---- extended -------------------------------------------------------------
        new() { Group = "Player",  Label = "survival",       Command = "survival" },
        new() { Group = "Player",  Label = "loghack",        Command = "loghack",     Tip = "Carry unlimited logs." },
        new() { Group = "Player",  Label = "crouchtoggle",   Command = "crouchtoggle" },
        new() { Group = "Player",  Label = "sprinttoggle",   Command = "sprinttoggle" },
        new() { Group = "Player",  Label = "fakedrown",      Command = "fakedrown" },
        new() { Group = "Player",  Label = "sleepcooldown",  Command = "sleepcooldown" },
        new() { Group = "Player",  Label = "heal me",        Command = "heallocalplayer",      Kind = CheatKind.Action },
        new() { Group = "Player",  Label = "knock me down",  Command = "knockdownlocalplayer", Kind = CheatKind.Action },
        new() { Group = "Player",  Label = "kill me",        Command = "killlocalplayer",      Kind = CheatKind.Action, Tip = "Kills you outright." },

        new() { Group = "World & time", Label = "lock time of day", Command = "locktimeofday" },
        new() { Group = "World & time", Label = "forcerain",     Command = "forcerain" },
        new() { Group = "World & time", Label = "cloudenable",   Command = "cloudenable" },
        new() { Group = "World & time", Label = "cloudshadows",  Command = "cloudshadowsenable" },
        new() { Group = "World & time", Label = "togglegrass",   Command = "togglegrass",  Tip = "RedLoader-added. Source-verified." },
        new() { Group = "World & time", Label = "unlockseason",  Command = "unlockseason" },
        new() { Group = "World & time", Label = "noforest",      Command = "noforest",     Kind = CheatKind.Action, Tip = "Removes Trees, Bushes and SmallTree objects. Source-verified." },
        new() { Group = "World & time", Label = "regrowalltrees", Command = "regrowalltrees", Kind = CheatKind.Action },
        new() { Group = "World & time", Label = "treescutall",   Command = "treescutall",  Kind = CheatKind.Action },
        new() { Group = "World & time", Label = "jumptimeofday", Command = "jumptimeofday", Kind = CheatKind.Action },

        new() { Group = "Building", Label = "buildermode",        Command = "buildermode" },
        new() { Group = "Building", Label = "structure ghosts",   Command = "enablestructureghosts" },
        new() { Group = "Building", Label = "instantbookbuild",   Command = "instantbookbuild" },
        new() { Group = "Building", Label = "finishblueprints",   Command = "finishblueprints", Kind = CheatKind.Action, Tip = "Completes all placed blueprints. Source-verified." },
        new() { Group = "Building", Label = "cancelblueprints",   Command = "cancelblueprints", Kind = CheatKind.Action, Tip = "Cancels all placed blueprints. Source-verified." },

        new() { Group = "Enemies & NPCs", Label = "aigodmode",     Command = "aigodmode",     Tip = "Enemies become invincible." },
        new() { Group = "Enemies & NPCs", Label = "AI ignores me", Command = "aighostplayer", Tip = "aighostplayer — RedLoader-added. Source-verified." },
        new() { Group = "Enemies & NPCs", Label = "removeliving",  Command = "removeliving",  Kind = CheatKind.Action },
        new() { Group = "Enemies & NPCs", Label = "removedead",    Command = "removedead",    Kind = CheatKind.Action },
        new() { Group = "Enemies & NPCs", Label = "creepyattackparty", Command = "creepyattackparty", Kind = CheatKind.Action },
        new() { Group = "Enemies & NPCs", Label = "creepyvillage", Command = "creepyvillage", Kind = CheatKind.Action },

        new() { Group = "Items", Label = "addallitems",    Command = "addallitems",    Kind = CheatKind.Action },
        new() { Group = "Items", Label = "refillcontainers", Command = "refillcontainers", Kind = CheatKind.Action },
        new() { Group = "Items", Label = "clearpickups",   Command = "clearpickups",   Kind = CheatKind.Action, Tip = "Removes dropped/spawned world pickups. Handy after bulk spawning." },
        new() { Group = "Items", Label = "removeallitems", Command = "removeallitems", Kind = CheatKind.Action, Tip = "Empties your inventory." },

        new() { Group = "Display & camera", Label = "showfps",        Command = "showfps",  Tip = "Verified both ways." },
        new() { Group = "Display & camera", Label = "showhud",        Command = "showhud" },
        new() { Group = "Display & camera", Label = "showui",         Command = "showui" },
        new() { Group = "Display & camera", Label = "showvitals",     Command = "showvitals" },
        new() { Group = "Display & camera", Label = "togglevsync",    Command = "togglevsync" },
        new() { Group = "Display & camera", Label = "freecamera",     Command = "freecamera" },
        new() { Group = "Display & camera", Label = "xfreecam",       Command = "xfreecam",  Kind = CheatKind.Flip, Tip = "RedLoader's free camera. Source-verified." },
        new() { Group = "Display & camera", Label = "toggleoverlay",  Command = "toggleoverlay",     Kind = CheatKind.Flip },
        new() { Group = "Display & camera", Label = "player stats overlay", Command = "toggleplayerstats", Kind = CheatKind.Flip },
    };
}
