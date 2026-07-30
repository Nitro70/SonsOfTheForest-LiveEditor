using System.Reflection;
using TheForest;

namespace LiveEditor.Core;

public enum ConsoleArgKind
{
    /// <summary>Command takes a string argument, e.g. godmode "on".</summary>
    String,
    /// <summary>Command takes no meaningful argument (its param is Il2CppSystem.Object).</summary>
    None,
}

public sealed record ConsoleCommandInfo(
    string Name,
    ConsoleArgKind ArgKind,
    bool Hidden,
    MethodInfo Method);

/// <summary>
/// Catalog of the game's built-in debug-console commands.
///
/// The console keeps two separate lists, and the difference is what "hidden" means:
///   _availableConsoleMethods - every command the console can dispatch
///   _commandShortList        - only the subset offered by autocomplete in the UI
/// A command present in the first but absent from the second is fully functional
/// yet invisible to a player typing in the console. We enumerate from the game's
/// own proxy methods (so nothing can be missed) and mark that hidden flag, then
/// invoke by MethodInfo directly — which bypasses autocomplete entirely.
/// </summary>
public static class ConsoleCatalog
{
    private static List<ConsoleCommandInfo>? _cache;

    /// <summary>
    /// True only if we could actually read the console's autocomplete list. When
    /// false the Hidden flags are all defaults and mean nothing — the console
    /// object doesn't exist until a world is loaded.
    /// </summary>
    public static bool HiddenDetectionAvailable { get; private set; }

    public static IReadOnlyList<ConsoleCommandInfo> Commands => _cache ??= Build();

    /// <summary>Drop the cache so Hidden flags are recomputed (e.g. after entering a world).</summary>
    public static void Invalidate()
    {
        _cache = null;
        HiddenDetectionAvailable = false;
    }

    public static bool TryGet(string name, out ConsoleCommandInfo info)
    {
        foreach (var c in Commands)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                info = c;
                return true;
            }
        }
        info = null!;
        return false;
    }

    private static List<ConsoleCommandInfo> Build()
    {
        var visible = GetAutocompleteVisibleNames();
        HiddenDetectionAvailable = visible.Count > 0;
        var result = new List<ConsoleCommandInfo>();

        // The Il2CppInterop-generated proxy for DebugConsole exposes each built-in
        // command as a real managed method named _<command>(string|Il2CppSystem.Object).
        foreach (var m in typeof(DebugConsole).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.Name.Length < 2 || m.Name[0] != '_') continue;
            if (!char.IsLetter(m.Name[1])) continue;

            var ps = m.GetParameters();
            if (ps.Length != 1) continue;

            var argKind = ps[0].ParameterType == typeof(string)
                ? ConsoleArgKind.String
                : ConsoleArgKind.None;

            var name = m.Name.Substring(1);
            var hidden = visible.Count > 0 && !visible.Contains(name.ToLowerInvariant());
            result.Add(new ConsoleCommandInfo(name, argKind, hidden, m));
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>Names the console's autocomplete will actually show. Empty set if unreadable.</summary>
    private static HashSet<string> GetAutocompleteVisibleNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var console = DebugConsole.Instance;
            if (console == null) return set;

            var shortList = console._commandShortList;
            if (shortList == null) return set;

            for (var i = 0; i < shortList.Count; i++)
            {
                var entry = shortList[i];
                if (string.IsNullOrEmpty(entry)) continue;
                // entries may carry usage text after the command word
                var word = entry.Split(' ')[0].TrimStart('_');
                set.Add(word.ToLowerInvariant());
            }
        }
        catch
        {
            set.Clear(); // can't tell — treat everything as visible rather than lie
        }
        return set;
    }

    /// <summary>Dynamic commands registered at runtime (by the SDK or other mods).</summary>
    public static List<string> GetDynamicCommandNames()
    {
        var names = new List<string>();
        try
        {
            // static on DebugConsole, not per-instance
            var dyn = DebugConsole._dynamicCommands;
            if (dyn == null) return names;

            foreach (var key in dyn.Keys)
            {
                if (!string.IsNullOrEmpty(key)) names.Add(key);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // dictionary shape changed or not ready; report what we have
        }
        return names;
    }
}
