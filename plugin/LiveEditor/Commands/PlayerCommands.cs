using System.Text.Json;
using System.Text.Json.Nodes;
using LiveEditor.Core;
using Sons.StatSystem;
using TheForest.Utils;

namespace LiveEditor.Commands;

/// <summary>
/// Player vitals.
///
/// WHY THIS DOES NOT WRITE Stat._currentValue DIRECTLY (VERIFIED, dump.cs):
///
///   * Every vital is a Sons.StatSystem.Stat carrying its own _regenRate, _fadeRate
///     and _baseValue, and Stat.Update(worldPosition, deltaTime) is driven from
///     Vitals.Update() every frame. A raw SetCurrentValue is therefore a value the
///     game is free to walk straight back.
///
///   * Health is worse than that: Vitals holds a SECOND value, _targetHealth, which
///     is the ceiling natural regeneration heals up to (hence GetTargetHealthFactor,
///     IsHealing, and the private SetTargetHealth). Raising _health above the target
///     without moving the target is undone by the very next Vitals.Update().
///     That is exactly what "I can read my stats but I can't set health" looks like.
///
/// So writes go through Vitals' own public API — SetHealth/SetStamina/SetRest/... —
/// the same entry points the game's own consumables and debug commands use, and the
/// health path raises _targetHealth FIRST so the new value is not immediately
/// regenerated away.
///
/// Two further corrections to the old mapping:
///   * "rested" is Vitals._rested (RestedStat, the sleep meter), NOT _vitality.
///     Vitality is the separate stamina/health ceiling stat (GetResolvedMaxVitality,
///     _maxVitalityLostWhenFreezing). They were being conflated.
///   * _temperature was never exposed at all; it is read-only here because Vitals
///     offers no setter for it.
/// </summary>
internal sealed class StatSpec
{
    public string Name = "";
    public Func<Vitals, Stat?> Get = _ => null;

    /// <summary>Null means the game exposes no setter for this stat — read-only.</summary>
    public Action<Vitals, float>? Set;

    /// <summary>The game's own "fill it" call, when it has one. Falls back to Set(max).</summary>
    public Action<Vitals>? Fill;

    public bool Writable => Set != null;
}

internal static class PlayerStats
{
    internal static bool TryGetVitals(out Vitals vitals, out string error)
    {
        vitals = null!;
        error = "";
        try
        {
            if (!SessionState.InWorld) { error = "no active player/world"; return false; }
            vitals = LocalPlayer.Vitals;
            if (vitals == null) { error = "LocalPlayer.Vitals is null"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static readonly StatSpec[] Specs =
    {
        new()
        {
            Name = "health",
            Get = v => v._health,
            // Target first, then current: SetHealth clamps against the regen ceiling,
            // so raising the ceiling afterwards would leave health pinned at the old one.
            Set = (v, f) => { v.SetTargetHealth(f); v.SetHealth(f); },
            Fill = v => v.SetFullHealth(),
        },
        new() { Name = "stamina",   Get = v => v._stamina,   Set = (v, f) => v.SetStamina(f),   Fill = v => v.SetFullStamina() },
        new() { Name = "hydration", Get = v => v._hydration, Set = (v, f) => v.SetHydration(f) },
        new() { Name = "fullness",  Get = v => v._fullness,  Set = (v, f) => v.SetFullness(f) },
        new() { Name = "rest",      Get = v => v._rested,    Set = (v, f) => v.SetRest(f),      Fill = v => v.SetFullRest() },
        new() { Name = "vitality",  Get = v => v._vitality,  Set = (v, f) => v.SetVitality(f),  Fill = v => v.SetFullVitality() },
        new() { Name = "sickness",  Get = v => v._sickness,  Set = (v, f) => v.SetSickness(f) },
        new() { Name = "strength",  Get = v => v._strength,  Set = (v, f) => v.SetStrength(f) },
        // No Vitals setter exists for temperature — surfaced so it can at least be read.
        new() { Name = "temperature", Get = v => v._temperature },
    };

    internal static readonly string[] All =
        { "health", "stamina", "hydration", "fullness", "rest", "vitality", "sickness", "strength", "temperature" };

    /// <summary>Aliases kept so older callers and natural phrasing both resolve.</summary>
    private static string Canonical(string name) => name.Trim().ToLowerInvariant() switch
    {
        "thirst" or "water" => "hydration",
        "hunger" or "food" => "fullness",
        "rested" or "sleep" or "energy" => "rest",
        "temp" => "temperature",
        var other => other,
    };

    internal static StatSpec? SpecFor(string name)
    {
        var canon = Canonical(name);
        foreach (var s in Specs) if (s.Name == canon) return s;
        return null;
    }

    internal static Stat? ByName(Vitals v, string name) => SpecFor(name)?.Get(v);

    internal static JsonObject Describe(Vitals v, StatSpec spec)
    {
        var s = spec.Get(v);
        if (s == null) return new JsonObject { ["available"] = false };
        try
        {
            var o = new JsonObject
            {
                ["current"] = Finite(s._currentValue),
                ["max"] = Finite(s._max),
                ["min"] = Finite(s._min),
                // Surfaced because it explains why a stat "won't stay set": a non-zero
                // rate means the game is actively walking it back toward _baseValue.
                ["regenRate"] = Finite(s._regenRate),
                ["fadeRate"] = Finite(s._fadeRate),
                ["writable"] = spec.Writable,
            };
            if (!spec.Writable) o["note"] = "read-only — Vitals exposes no setter";
            if (spec.Name == "health") o["targetHealth"] = Finite(v._targetHealth);
            return o;
        }
        catch
        {
            return new JsonObject { ["available"] = false };
        }
    }

    /// <summary>
    /// System.Text.Json refuses to write NaN/Infinity, and that throw happens at
    /// serialization time on the socket thread — long after this object was built.
    /// Emit null for non-finite values so one odd stat cannot break the response.
    /// </summary>
    private static JsonNode? Finite(float f) => float.IsFinite(f) ? JsonValue.Create(f) : null;
}

public sealed class PlayerGetCommand : ICommandHandler
{
    public string Name => "player.get";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!PlayerStats.TryGetVitals(out var v, out var err))
            return CommandResult.Error(ErrorCodes.NotInWorld, err);

        var stats = new JsonObject();
        foreach (var n in PlayerStats.All)
        {
            var spec = PlayerStats.SpecFor(n);
            if (spec != null) stats[n] = PlayerStats.Describe(v, spec);
        }

        return CommandResult.Ok(new JsonObject { ["stats"] = stats });
    }
}

/// <summary>player.set {stat, value} — or {stat, max:true} to fill it.</summary>
public sealed class PlayerSetCommand : ICommandHandler
{
    public string Name => "player.set";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!PlayerStats.TryGetVitals(out var v, out var err))
            return CommandResult.Error(ErrorCodes.NotInWorld, err);

        if (args is null || !args.Value.TryGetProperty("stat", out var statEl))
            return CommandResult.Error(ErrorCodes.BadArgs, "missing 'stat'");

        var statName = statEl.GetString() ?? "";
        var spec = PlayerStats.SpecFor(statName);
        if (spec == null)
            return CommandResult.Error(ErrorCodes.BadArgs,
                $"unknown stat '{statName}'. valid: {string.Join(", ", PlayerStats.All)}");

        var stat = spec.Get(v);
        if (stat == null)
            return CommandResult.Error(ErrorCodes.ExecFailed, $"'{spec.Name}' is not present on this player");
        if (!spec.Writable)
            return CommandResult.Error(ErrorCodes.Unsupported,
                $"'{spec.Name}' is read-only — Vitals exposes no setter for it");

        var fillToMax = args.Value.TryGetProperty("max", out var maxEl)
                        && maxEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                        && maxEl.GetBoolean();

        float target;
        if (fillToMax)
        {
            target = stat._max;
        }
        else if (args.Value.TryGetProperty("value", out var valEl) && valEl.ValueKind == JsonValueKind.Number)
        {
            target = valEl.GetSingle();
        }
        else
        {
            return CommandResult.Error(ErrorCodes.BadArgs, "provide numeric 'value', or 'max': true");
        }

        var before = stat._currentValue;
        try
        {
            // Prefer the game's own fill call when there is one: SetFullHealth also
            // moves _targetHealth and fires the same side effects the game expects.
            if (fillToMax && spec.Fill != null) spec.Fill(v);
            else spec.Set!(v, target);
        }
        catch (Exception ex)
        {
            return CommandResult.Error(ErrorCodes.ExecFailed, $"Vitals setter threw: {ex.Message}");
        }

        var after = stat._currentValue;
        var result = new JsonObject
        {
            ["stat"] = spec.Name,
            ["requested"] = target,
            ["before"] = before,
            ["after"] = after,
            ["max"] = stat._max,
            // Honest reporting: the caller should not have to infer from before/after
            // whether the game actually accepted the write.
            ["changed"] = Math.Abs(after - before) > 0.0001f,
        };

        if (Math.Abs(after - target) > 0.01f)
            result["note"] = $"the game settled on {after} rather than {target} — it clamps to [{stat._min}, {stat._max}]";
        else if (stat._regenRate > 0f || stat._fadeRate > 0f)
            result["note"] = $"this stat has a regen/fade rate ({stat._regenRate}/{stat._fadeRate}), so the game will keep moving it on its own";

        if (spec.Name == "health") result["targetHealth"] = v._targetHealth;

        return CommandResult.Ok(result);
    }
}

/// <summary>player.restore — fills health, stamina, hydration, fullness, rest and vitality; clears sickness.</summary>
public sealed class PlayerRestoreCommand : ICommandHandler
{
    public string Name => "player.restore";

    public CommandResult Execute(JsonElement? args, CommandContext ctx)
    {
        if (!PlayerStats.TryGetVitals(out var v, out var err))
            return CommandResult.Error(ErrorCodes.NotInWorld, err);

        var changed = new JsonObject();
        var failed = new JsonArray();

        foreach (var n in new[] { "health", "stamina", "hydration", "fullness", "rest", "vitality" })
        {
            var spec = PlayerStats.SpecFor(n);
            var s = spec?.Get(v);
            if (spec == null || s == null) continue;
            try
            {
                if (spec.Fill != null) spec.Fill(v);
                else spec.Set!(v, s._max);
                changed[n] = s._currentValue;
            }
            catch (Exception ex)
            {
                failed.Add($"{n}: {ex.Message}");
            }
        }

        // Sickness is the inverse: zero is healthy.
        var sick = PlayerStats.SpecFor("sickness");
        var sickStat = sick?.Get(v);
        if (sick != null && sickStat != null)
        {
            try { sick.Set!(v, sickStat._min); changed["sickness"] = sickStat._currentValue; }
            catch (Exception ex) { failed.Add($"sickness: {ex.Message}"); }
        }

        var result = new JsonObject { ["restored"] = changed };
        if (failed.Count > 0) result["failed"] = failed;
        return CommandResult.Ok(result);
    }
}
