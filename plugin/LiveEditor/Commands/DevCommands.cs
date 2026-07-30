using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using Sons.Gui;
using Sons.Save;
using TheForest;
using UnityEngine;

namespace LiveEditor.Commands;

/// <summary>
/// Loads a save without touching the main menu, so a world can be entered
/// unattended. This mirrors exactly what SonsSdk's own `--savegame` launch arg
/// does (SdkEntryPoint.OnSdkInitialized), except the save type is selectable —
/// the SDK hardcodes SinglePlayer, which is useless on a profile that only has
/// Multiplayer saves.
/// </summary>
public sealed class DevLoadSaveCommand : ICommandHandler
{
    public string Name => "dev.loadsave";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (args is null || !args.Value.TryGetProperty("saveId", out var idEl))
            return CommandResult.Error(ErrorCodes.BadArgs, "missing 'saveId'");

        var saveId = idEl.GetInt32();
        var typeVal = args.Value.TryGetProperty("type", out var t) ? t.GetInt32() : 1; // default Multiplayer
        var saveType = (SaveGameType)typeVal;

        try
        {
            var menus = Resources.FindObjectsOfTypeAll<LoadMenu>();
            if (menus == null || menus.Count == 0)
                return CommandResult.Error(ErrorCodes.ExecFailed, "no LoadMenu instance found");

            LoadMenu? menu = null;
            for (var i = 0; i < menus.Count; i++)
            {
                if (menus[i] != null) { menu = menus[i]; break; }
            }
            if (menu == null)
                return CommandResult.Error(ErrorCodes.ExecFailed, "LoadMenu instances were all null");

            var data = SaveGameManager.GetData<GameStateData>(saveType, saveId);
            if (data == null)
                return CommandResult.Error(ErrorCodes.BadArgs,
                    $"no save data for id={saveId} type={saveType}");

            menu.LoadSlotActivated(saveId, data);
            return CommandResult.Ok(new JsonObject
            {
                ["loading"] = true,
                ["saveId"] = saveId,
                ["type"] = saveType.ToString(),
            });
        }
        catch (Exception ex)
        {
            return CommandResult.Error(ErrorCodes.ExecFailed, $"load failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Definitive hidden-command analysis, straight from the console's own two
/// registries rather than inferred:
///   _availableConsoleMethods - every command the console can dispatch
///   _commandShortList        - the subset autocomplete offers while typing
/// We call BuildCommandShortList() first so the comparison is valid even if the
/// console UI has never been opened this session.
/// </summary>
public sealed class ConsoleIntrospectCommand : ICommandHandler
{
    public string Name => "console.introspect";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        var console = DebugConsole.Instance;
        if (console == null)
            return CommandResult.Error(ErrorCodes.NotInWorld,
                "console does not exist yet — load a world first (see dev.loadsave)");

        var forced = false;
        try
        {
            console.BuildCommandShortList();
            forced = true;
        }
        catch
        {
            // fall back to whatever state the list is already in
        }

        var available = new List<string>();
        try
        {
            var dict = console._availableConsoleMethods;
            if (dict != null)
            {
                foreach (var k in dict.Keys)
                    if (!string.IsNullOrEmpty(k)) available.Add(k);
            }
        }
        catch (Exception ex)
        {
            return CommandResult.Error(ErrorCodes.ExecFailed,
                $"could not read _availableConsoleMethods: {ex.Message}");
        }

        var shortList = new List<string>();
        try
        {
            var sl = console._commandShortList;
            if (sl != null)
            {
                for (var i = 0; i < sl.Count; i++)
                {
                    var e = sl[i];
                    if (!string.IsNullOrEmpty(e)) shortList.Add(e.Split(' ')[0].TrimStart('_'));
                }
            }
        }
        catch
        {
            // leave empty; reported below
        }

        var visible = new HashSet<string>(shortList, StringComparer.OrdinalIgnoreCase);
        var hidden = new JsonArray();
        foreach (var name in available.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!visible.Contains(name.TrimStart('_'))) hidden.Add(name);
        }

        var availableArr = new JsonArray();
        foreach (var a in available.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) availableArr.Add(a);
        var shortArr = new JsonArray();
        foreach (var s in shortList.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) shortArr.Add(s);

        return CommandResult.Ok(new JsonObject
        {
            ["forcedRebuild"] = forced,
            ["availableCount"] = available.Count,
            ["autocompleteCount"] = shortList.Count,
            ["hiddenCount"] = hidden.Count,
            ["hidden"] = hidden,
            ["available"] = availableArr,
            ["autocomplete"] = shortArr,
            ["blockConsole"] = SafeBool(() => console._blockConsole),
            ["isConsoleAllowed"] = SafeBool(() => console.IsConsoleAllowed()),
            ["isConsoleBlocked"] = SafeBool(() => console.IsConsoleBlocked()),
        });
    }

    private static bool SafeBool(Func<bool> f)
    {
        try { return f(); } catch { return false; }
    }
}

/// <summary>
/// cheats.force — toggles the local cheat-gate bypass (see CheatGateBypass).
/// On by default; call with {"enabled": false} to hand control back to the game.
/// </summary>
public sealed class CheatsForceCommand : ICommandHandler
{
    public string Name => "cheats.force";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (args is not null && args.Value.TryGetProperty("enabled", out var en))
        {
            CheatGateBypass.Enabled = en.GetBoolean();
            if (CheatGateBypass.Enabled) CheatGateBypass.Apply();
        }

        return CommandResult.Ok(new JsonObject
        {
            ["enabled"] = CheatGateBypass.Enabled,
            ["consoleState"] = CheatGateBypass.DescribeState(),
            ["mode"] = SessionState.ModeString,
            ["note"] = "local only: re-opens this client's console. Does NOT grant host authority, so server-owned actions still will not replicate from a client.",
        });
    }
}

/// <summary>Force the console fully open/allowed: cheats on, block off.</summary>
public sealed class ConsoleUnlockCommand : ICommandHandler
{
    public string Name => "console.unlock";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        var console = DebugConsole.Instance;
        if (console == null)
            return CommandResult.Error(ErrorCodes.NotInWorld, "console does not exist yet");

        var report = new JsonObject();
        try { DebugConsole.SetCheatsAllowed(true); report["setCheatsAllowed"] = true; }
        catch (Exception ex) { report["setCheatsAllowed"] = $"failed: {ex.Message}"; }

        try { console.SetBlockConsole(false); report["setBlockConsole"] = true; }
        catch (Exception ex) { report["setBlockConsole"] = $"failed: {ex.Message}"; }

        try { console.BuildCommandShortList(); report["rebuiltShortList"] = true; }
        catch (Exception ex) { report["rebuiltShortList"] = $"failed: {ex.Message}"; }

        try
        {
            report["isConsoleAllowed"] = console.IsConsoleAllowed();
            report["isConsoleBlocked"] = console.IsConsoleBlocked();
        }
        catch { /* best effort */ }

        ConsoleCatalog.Invalidate();
        return CommandResult.Ok(report);
    }
}
