using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using TheForest;

namespace LiveEditor.Commands;

public sealed class ConsoleListCommand : ICommandHandler
{
    public string Name => "console.list";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        var includeHiddenOnly = args?.TryGetProperty("hiddenOnly", out var h) == true && h.GetBoolean();
        var filter = args?.TryGetProperty("filter", out var f) == true ? f.GetString() : null;

        var builtin = new JsonArray();
        var hiddenCount = 0;
        foreach (var c in ConsoleCatalog.Commands)
        {
            if (c.Hidden) hiddenCount++;
            if (includeHiddenOnly && !c.Hidden) continue;
            if (!string.IsNullOrEmpty(filter) &&
                c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

            builtin.Add(new JsonObject
            {
                ["name"] = c.Name,
                ["args"] = c.ArgKind == ConsoleArgKind.String ? "string" : "none",
                ["hidden"] = c.Hidden,
            });
        }

        var dynamic = new JsonArray();
        foreach (var d in ConsoleCatalog.GetDynamicCommandNames()) dynamic.Add(d);

        return CommandResult.Ok(new JsonObject
        {
            ["builtin"] = builtin,
            ["dynamic"] = dynamic,
            ["totalBuiltin"] = ConsoleCatalog.Commands.Count,
            ["totalHidden"] = hiddenCount,
            // The autocomplete list only exists once the console does (in-world).
            // Without it we cannot tell hidden from visible, so say so rather than
            // reporting a misleading "0 hidden".
            ["hiddenDetectionAvailable"] = ConsoleCatalog.HiddenDetectionAvailable,
        });
    }
}

/// <summary>
/// Runs any console command, including ones hidden from the in-game autocomplete.
/// Dispatch prefers invoking the command's method directly (which ignores console
/// visibility and open/closed state); dynamic commands fall back to SendCommand.
/// </summary>
public sealed class ConsoleExecCommand : ICommandHandler
{
    public string Name => "console.exec";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (args is null || !args.Value.TryGetProperty("line", out var lineEl))
            return CommandResult.Error(ErrorCodes.BadArgs, "missing 'line'");

        var line = (lineEl.GetString() ?? "").Trim();
        if (line.Length == 0)
            return CommandResult.Error(ErrorCodes.BadArgs, "empty command line");

        var space = line.IndexOf(' ');
        var cmdName = space < 0 ? line : line.Substring(0, space);
        var cmdArgs = space < 0 ? "" : line.Substring(space + 1).Trim();

        // spawnitem consults ItemData._canBeSpawned and refuses 60 items (artifact
        // pieces, blueprints, notes). Clear that gate for whatever was asked for, so
        // passthrough behaves like every other command instead of silently failing on
        // a subset. Runtime-only flag; see SpawnUnlock.
        var unlocked = false;
        if (cmdName.Equals("spawnitem", StringComparison.OrdinalIgnoreCase) ||
            cmdName.Equals("spawnpickup", StringComparison.OrdinalIgnoreCase))
        {
            unlocked = TryUnlockForSpawn(cmdArgs);
        }

        // The console object only exists once a world is loaded; at the title screen
        // there is nothing to dispatch to. That's a state problem, not an exec failure.
        var console = DebugConsole.Instance;
        if (console == null)
            return CommandResult.Error(ErrorCodes.NotInWorld,
                "the debug console does not exist yet — load into a world first");

        if (ConsoleCatalog.TryGet(cmdName, out var info))
        {
            // End() exactly once: calling it in both catch and finally discarded the
            // captured output on the error path, which is when it matters most.
            List<string> captured;
            ConsoleOutputCapture.Begin();
            try
            {
                var arg = info.ArgKind == ConsoleArgKind.String ? (object?)cmdArgs : null;
                info.Method.Invoke(console, new[] { arg });
            }
            catch (Exception ex)
            {
                var partial = ConsoleOutputCapture.End();
                var inner = ex.InnerException ?? ex;
                var err = new JsonObject
                {
                    ["command"] = info.Name,
                    ["output"] = ToJsonArray(partial),
                };
                return CommandResult.Error(ErrorCodes.ExecFailed,
                    $"{cmdName} threw: {inner.GetType().Name}: {inner.Message}", err);
            }
            captured = ConsoleOutputCapture.End();

            var ok = new JsonObject
            {
                ["command"] = info.Name,
                ["argsPassed"] = info.ArgKind == ConsoleArgKind.String ? cmdArgs : null,
                ["dispatch"] = "direct",
                ["wasHidden"] = info.Hidden,
                ["output"] = ToJsonArray(captured),
                // So an empty output array can be told apart from a broken capture.
                ["captureSource"] = ConsoleOutputCapture.UnityHookInstalled ? "unity-log-hook" : "patches-only",
            };
            if (unlocked) ok["spawnGateBypassed"] = true;
            return CommandResult.Ok(ok);
        }

        // Not a built-in: could be a dynamically registered command (SDK/other mods).
        List<string> dynCaptured;
        ConsoleOutputCapture.Begin();
        try
        {
            console.SendCommand(line);
        }
        catch (Exception ex)
        {
            ConsoleOutputCapture.End();
            return CommandResult.Error(ErrorCodes.ExecFailed, $"SendCommand threw: {ex.Message}");
        }
        dynCaptured = ConsoleOutputCapture.End();

        return CommandResult.Ok(new JsonObject
        {
            ["command"] = cmdName,
            ["dispatch"] = "sendcommand",
            ["output"] = ToJsonArray(dynCaptured),
            ["note"] = "not a built-in; forwarded to the console's own dispatcher",
        });
    }

    private static JsonArray ToJsonArray(IEnumerable<string> lines)
    {
        var arr = new JsonArray();
        foreach (var l in lines) arr.Add(l);
        return arr;
    }

    /// <summary>
    /// Resolves the first token of a spawnitem argument string to an item and clears
    /// its spawn gate. Accepts an id or any name form; a trailing amount is ignored
    /// for lookup purposes.
    /// </summary>
    private static bool TryUnlockForSpawn(string cmdArgs)
    {
        if (string.IsNullOrWhiteSpace(cmdArgs)) return false;
        var token = cmdArgs.Split(' ')[0];
        try
        {
            if (int.TryParse(token, out var id))
            {
                return Sons.Items.Core.ItemDatabaseManager.TryFindItemById(id, out var byId)
                       && SpawnUnlock.Unlock(byId);
            }
            return ItemLookup.TryResolveByName(token, out var byName) && SpawnUnlock.Unlock(byName);
        }
        catch
        {
            return false;
        }
    }
}
