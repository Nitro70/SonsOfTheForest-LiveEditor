using System.IO;
using Microsoft.Win32;

namespace LiveEditorApp;

/// <summary>
/// Finds the Sons Of The Forest install without asking the user.
///
/// Steam scatters games across "library folders" that can live on any drive, so a
/// single hardcoded path only ever works on one machine. The order below goes from
/// cheapest and most authoritative to slowest:
///
///   1. gamedir.txt next to the executable   - manual override, always wins
///   2. Steam's registry entry -> libraryfolders.vdf -> each library  - the real answer
///   3. common install roots on every fixed drive  - covers a Steam-less/moved copy
///
/// Steps 1-2 are effectively instant. Step 3 only probes a handful of known paths per
/// drive rather than walking the filesystem, so it stays fast even with many drives.
/// </summary>
public static class GameLocator
{
    private const string ExeName = "SonsOfTheForest.exe";
    private const string GameFolder = @"steamapps\common\Sons Of The Forest";

    /// <summary>Null when the game genuinely could not be found.</summary>
    public static string? Find()
    {
        var over = FromOverrideFile();
        if (over != null) return over;

        foreach (var lib in SteamLibraries())
        {
            var candidate = Path.Combine(lib, GameFolder);
            if (IsGameDir(candidate)) return candidate;
        }

        return FromDriveScan();
    }

    /// <summary>Where a user should write a path if detection ever fails them.</summary>
    public static string OverrideFilePath =>
        Path.Combine(AppContext.BaseDirectory, "gamedir.txt");

    public static bool IsGameDir(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, ExeName));

    private static string? FromOverrideFile()
    {
        try
        {
            if (!File.Exists(OverrideFilePath)) return null;
            var dir = File.ReadAllText(OverrideFilePath).Trim().Trim('"');
            return IsGameDir(dir) ? dir : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var steam = SteamRoot();
        if (steam == null) yield break;

        yield return steam;

        // libraryfolders.vdf lists every extra library. Rather than write a VDF parser
        // for one field, pull out the quoted "path" values — the format nests but the
        // path entries are unambiguous.
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        string text;
        try { text = File.ReadAllText(vdf); } catch { yield break; }

        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
        {
            var p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (!string.IsNullOrWhiteSpace(p)) yield return p;
        }
    }

    private static string? SteamRoot()
    {
        foreach (var (hive, key, value) in new[]
                 {
                     (Registry.CurrentUser,  @"Software\Valve\Steam",                  "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam",      "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam",                  "InstallPath"),
                 })
        {
            try
            {
                using var k = hive.OpenSubKey(key);
                if (k?.GetValue(value) is string s && Directory.Exists(s))
                    return s.Replace('/', '\\');
            }
            catch { /* registry access can be denied; try the next one */ }
        }
        return null;
    }

    private static string? FromDriveScan()
    {
        string[] roots =
        {
            GameFolder,
            @"SteamLibrary\" + GameFolder,
            @"Steam\" + GameFolder,
            @"Games\" + GameFolder,
            @"Program Files (x86)\Steam\" + GameFolder,
            @"Program Files\Steam\" + GameFolder,
            @"SteamLibrary\steamapps\common\SonsOfTheForest",
        };

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
            foreach (var rel in roots)
            {
                try
                {
                    var candidate = Path.Combine(drive.RootDirectory.FullName, rel);
                    if (IsGameDir(candidate)) return candidate;
                }
                catch { /* unreadable drive; move on */ }
            }
        }
        return null;
    }
}
