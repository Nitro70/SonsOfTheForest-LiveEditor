using System.Text;
using Sons.Items.Core;

namespace LiveEditor.Core;

/// <summary>
/// Friendly item naming. The game ships two names per item:
///   _name       internal identifier, e.g. "ModernAxe", "Log_Legacy"
///   _uiData     localized UI data, whose GetTitleLocalized() is the string the
///               game actually shows the player, e.g. "Modern Axe"
/// We surface the localized title as displayName and match against every form so
/// callers can say "Modern axe", "modern_axe", "ModernAxe" or 356 interchangeably.
/// </summary>
public static class ItemNames
{
    public static string DisplayName(ItemData item)
    {
        // Preferred: the game's own localized title.
        try
        {
            var ui = item._uiData;
            if (ui != null)
            {
                var localized = ui.GetTitleLocalized();
                if (!string.IsNullOrWhiteSpace(localized) && !LooksLikeRawKey(localized))
                    return localized;

                if (!string.IsNullOrWhiteSpace(ui._title) && !LooksLikeRawKey(ui._title))
                    return ui._title;
            }
        }
        catch
        {
            // localization not ready / item has no ui data — fall through
        }

        // Fallback: humanize the internal name ("ModernAxe" -> "Modern Axe").
        return Humanize(item.Name);
    }

    /// <summary>A missing translation usually comes back as the raw key, e.g. "Item_ModernAxe_Title".</summary>
    private static bool LooksLikeRawKey(string s) =>
        s.Contains('_') && !s.Contains(' ');

    /// <summary>"ModernAxe" -> "Modern Axe"; "Log_Legacy" -> "Log Legacy"; "I9mmAmmo" -> "I9mm Ammo".</summary>
    public static string Humanize(string internalName)
    {
        if (string.IsNullOrEmpty(internalName)) return internalName ?? "";

        var cleaned = internalName.Replace('_', ' ').Replace('-', ' ');
        var sb = new StringBuilder(cleaned.Length + 8);
        for (var i = 0; i < cleaned.Length; i++)
        {
            var c = cleaned[i];
            if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(cleaned[i - 1]))
            {
                var prev = cleaned[i - 1];
                var nextIsLower = i + 1 < cleaned.Length && char.IsLower(cleaned[i + 1]);
                // Break before a capital that starts a new word, but keep runs of
                // caps together (e.g. "GPSLocator" -> "GPS Locator").
                if (char.IsLower(prev) || char.IsDigit(prev) || nextIsLower)
                    sb.Append(' ');
            }
            sb.Append(c);
        }

        var result = sb.ToString();
        while (result.Contains("  ")) result = result.Replace("  ", " ");
        return result.Trim();
    }

    /// <summary>Collapses a name to a comparable key: lowercase, alphanumerics only.</summary>
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
