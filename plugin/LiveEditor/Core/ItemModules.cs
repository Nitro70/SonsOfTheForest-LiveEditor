using System.Text.Json;
using System.Text.Json.Nodes;
using Sons.Inventory;
using Sons.Items.Core;

namespace LiveEditor.Core;

/// <summary>
/// Per-instance item data ("modules"). An ItemInstance owns a list of
/// ItemInstanceModule objects, which is how two copies of the same item id differ.
///
/// There are ten module types but they are NOT all yes/no switches, so this exposes
/// each one with the shape it actually has:
///
///   boolean  plating            ItemPlating.SetIsPlated        - solafite coating
///            pauseDecay         Perishable.PauseDecay          - food stops rotting
///            arrowUpgradeActive ArrowUpgrade.SetUpgradeActive
///            gpsActive/Pulse/Beep  GPSLocator flags
///            limbFresh          DismemberedLimb._isFresh
///   numeric  ammoCount          RangedWeapon.SetAmmoCount      - rounds loaded
///            ammoType           RangedWeapon.SetAmmoType       - ammo item id
///            volume / fill      VolumeContainer.CurrentVolume  - liquid held
///            variant            VisualVariant.SetVariant       - cosmetic index
///
/// Deliberately NOT exposed:
///   StructureElement - an element id plus an ingredient-amount array tied to the
///     building system; a bool would be meaningless and a wrong id risks bad state.
///   BloodReveal      - a Color of channel weights, not a flag.
///
/// Module lookup walks the _modules list and uses non-generic CheckForXModule()
/// helpers, avoiding ItemInstance.GetOrAddModule&lt;T&gt;(): IL2CPP only ships generic
/// instantiations that were AOT-compiled, so an uninstantiated one fails at runtime.
/// </summary>
public static class ItemModules
{
    private static T? Find<T>(ItemInstance instance) where T : ItemInstanceModule
    {
        try
        {
            var modules = instance?._modules;
            if (modules == null) return null;
            for (var i = 0; i < modules.Count; i++)
            {
                var cast = modules[i]?.TryCast<T>();
                if (cast != null) return cast;
            }
        }
        catch { }
        return null;
    }

    /// <summary>Runs the game's own "attach this module if the item supports it" helpers.</summary>
    private static void EnsureModules(ItemInstance instance)
    {
        void Try(Action a) { try { a(); } catch { } }
        Try(() => instance.CheckForItemPlatingModule());
        Try(() => instance.CheckForPerishableModule());
        Try(() => instance.CheckForArrowUpgradeModule());
        Try(() => instance.CheckForGpsLocatorModule());
        Try(() => instance.CheckForVolumeContainerModule());
        Try(() => instance.CheckForVisualVariantModule());
        Try(() => instance.CheckForDismemberedLimbModule());
    }

    public static bool CanBePlated(ItemData item)
    {
        try { return item._canBePlated; } catch { return false; }
    }

    /// <summary>
    /// Pushes a module change onto the item's live GameObject.
    ///
    /// This is the step that was missing, and it is why plating an item you already
    /// hold appeared to do nothing while spawning a pre-plated one worked:
    /// ItemPlatingItemInstanceModule.SetIsPlated() only writes the flag. The material
    /// swap and the damage multiplier come from the module's Refresh(itemInstance,
    /// itemObject) override, which the game runs when an item's GameObject is built.
    /// A freshly added item gets that for free; an item already in your hand does not,
    /// so the flag flipped and nothing visibly changed.
    ///
    /// ItemInstance.RefreshItemObject() re-runs that pass against the existing object
    /// (VERIFIED present: Sons.dll ItemInstance.RefreshItemObject(bool)).
    /// </summary>
    public static bool RefreshVisual(ItemInstance instance, out string error)
    {
        error = "";
        if (instance == null) { error = "no item instance"; return false; }
        try
        {
            // No live object means nothing to repaint — the data is set and the game
            // will apply it the next time the item is drawn. Not an error.
            if (instance.ItemObject == null)
            {
                error = "not currently spawned in the world or in hand — the change is stored and shows next time the item is drawn";
                return false;
            }
            instance.RefreshItemObject(true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"RefreshItemObject threw: {ex.Message}";
            return false;
        }
    }

    /// <summary>Reports which modules an instance currently has, and their values.</summary>
    public static JsonObject Describe(ItemInstance instance)
    {
        var o = new JsonObject();

        var plating = Find<ItemPlatingItemInstanceModule>(instance);
        if (plating != null)
            o["plating"] = new JsonObject { ["plated"] = Safe(() => plating.IsPlated), ["damageMultiplier"] = SafeF(() => plating.DamageMultiplier) };

        var perish = Find<PerishableItemInstanceModule>(instance);
        if (perish != null)
            o["perishable"] = new JsonObject { ["pauseDecay"] = Safe(() => perish.PauseDecay), ["timeRemaining"] = SafeF(() => perish.TimeRemainingInState) };

        var arrow = Find<ArrowUpgradeItemInstanceModule>(instance);
        if (arrow != null)
            o["arrowUpgrade"] = new JsonObject { ["active"] = Safe(() => arrow._isUpgradeActivated) };

        var gps = Find<GPSLocatorItemInstanceModule>(instance);
        if (gps != null)
            o["gpsLocator"] = new JsonObject
            {
                ["isActive"] = Safe(() => gps.IsActive),
                ["pulseIcon"] = Safe(() => gps.PulseIcon),
                ["beepInRange"] = Safe(() => gps.ShouldBeepWhenInRange),
            };

        var vol = Find<VolumeContainerItemInstanceModule>(instance);
        if (vol != null)
            o["volumeContainer"] = new JsonObject
            {
                ["current"] = SafeF(() => vol.CurrentVolume),
                ["max"] = SafeF(() => vol.MaxVolume),
                ["isFull"] = Safe(() => vol.IsFull),
            };

        var variant = Find<VisualVariantItemInstanceModule>(instance);
        if (variant != null)
            o["visualVariant"] = new JsonObject { ["variant"] = SafeI(() => variant._variantNum) };

        var ranged = Find<RangedWeaponItemInstanceModule>(instance);
        if (ranged != null)
            o["rangedWeapon"] = new JsonObject
            {
                ["ammoCount"] = SafeI(() => ranged._ammoCount),
                ["ammoType"] = SafeI(() => ranged._ammoType),
            };

        var limb = Find<DismemberedLimbItemInstanceModule>(instance);
        if (limb != null)
            o["dismemberedLimb"] = new JsonObject { ["isFresh"] = Safe(() => limb._isFresh) };

        var structure = Find<StructureElementItemInstanceModule>(instance);
        if (structure != null)
            o["structureElement"] = new JsonObject { ["elementId"] = SafeI(() => (int)structure.ElementId), ["readOnly"] = true };

        var blood = Find<BloodRevealItemInstanceModule>(instance);
        if (blood != null)
            o["bloodReveal"] = new JsonObject { ["note"] = "channel-weight Color, not a toggle", ["readOnly"] = true };

        return o;
    }

    /// <summary>
    /// Applies a caller-supplied module spec to one instance. Unknown keys and keys
    /// whose module is absent are reported back rather than silently dropped.
    /// </summary>
    public static void Apply(ItemInstance instance, JsonElement spec, List<string> applied, List<string> skipped)
    {
        EnsureModules(instance);

        foreach (var prop in spec.EnumerateObject())
        {
            var key = prop.Name;
            var v = prop.Value;

            switch (key.ToLowerInvariant())
            {
                case "plating" or "plated":
                    ApplyBool(key, v, Find<ItemPlatingItemInstanceModule>(instance), (m, b) => m.SetIsPlated(b), applied, skipped);
                    break;

                case "pausedecay":
                    ApplyBool(key, v, Find<PerishableItemInstanceModule>(instance), (m, b) => m.PauseDecay = b, applied, skipped);
                    break;

                case "arrowupgradeactive":
                    ApplyBool(key, v, Find<ArrowUpgradeItemInstanceModule>(instance), (m, b) => m.SetUpgradeActive(b), applied, skipped);
                    break;

                case "gpsactive":
                    ApplyBool(key, v, Find<GPSLocatorItemInstanceModule>(instance), (m, b) => m.IsActive = b, applied, skipped);
                    break;
                case "gpspulse":
                    ApplyBool(key, v, Find<GPSLocatorItemInstanceModule>(instance), (m, b) => m.PulseIcon = b, applied, skipped);
                    break;
                case "gpsbeep":
                    ApplyBool(key, v, Find<GPSLocatorItemInstanceModule>(instance), (m, b) => m.ShouldBeepWhenInRange = b, applied, skipped);
                    break;

                case "limbfresh":
                    ApplyBool(key, v, Find<DismemberedLimbItemInstanceModule>(instance), (m, b) => m._isFresh = b, applied, skipped);
                    break;

                case "ammocount":
                    ApplyInt(key, v, Find<RangedWeaponItemInstanceModule>(instance), (m, i) => m.SetAmmoCount(i), applied, skipped);
                    break;
                case "ammotype":
                    ApplyInt(key, v, Find<RangedWeaponItemInstanceModule>(instance), (m, i) => m.SetAmmoType(i), applied, skipped);
                    break;
                case "variant":
                    ApplyInt(key, v, Find<VisualVariantItemInstanceModule>(instance), (m, i) => m.SetVariant(i), applied, skipped);
                    break;

                case "volume":
                    ApplyFloat(key, v, Find<VolumeContainerItemInstanceModule>(instance), (m, f) => m.CurrentVolume = f, applied, skipped);
                    break;
                case "fill":
                    // factor 0..1 of the container's own max
                    ApplyFloat(key, v, Find<VolumeContainerItemInstanceModule>(instance),
                        (m, f) => m.CurrentVolume = m.MaxVolume * Math.Clamp(f, 0f, 1f), applied, skipped);
                    break;

                case "structureelement" or "bloodreveal":
                    skipped.Add($"{key}: not settable — structural/colour data, not a toggle");
                    break;

                default:
                    skipped.Add($"{key}: unknown module key");
                    break;
            }
        }
    }

    private static void ApplyBool<T>(string key, JsonElement v, T? module, Action<T, bool> set,
        List<string> applied, List<string> skipped) where T : ItemInstanceModule
    {
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            skipped.Add($"{key}: expected true/false");
            return;
        }
        if (module == null)
        {
            // Callers send every boolean key so the UI can be plain on/off, which means
            // most items receive keys for modules they simply do not have. Asking to
            // turn an absent module OFF is already true, so reporting it as "skipped"
            // would bury the one line that matters — asking to turn one ON.
            if (v.GetBoolean()) skipped.Add($"{key}: this item has no {typeof(T).Name}");
            return;
        }
        try { set(module, v.GetBoolean()); applied.Add($"{key}={v.GetBoolean()}"); }
        catch (Exception ex) { skipped.Add($"{key}: {ex.Message}"); }
    }

    private static void ApplyInt<T>(string key, JsonElement v, T? module, Action<T, int> set,
        List<string> applied, List<string> skipped) where T : ItemInstanceModule
    {
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var i))
        {
            skipped.Add($"{key}: expected a whole number");
            return;
        }
        if (module == null)
        {
            skipped.Add($"{key}: this item has no {typeof(T).Name}");
            return;
        }
        try { set(module, i); applied.Add($"{key}={i}"); }
        catch (Exception ex) { skipped.Add($"{key}: {ex.Message}"); }
    }

    private static void ApplyFloat<T>(string key, JsonElement v, T? module, Action<T, float> set,
        List<string> applied, List<string> skipped) where T : ItemInstanceModule
    {
        if (v.ValueKind != JsonValueKind.Number)
        {
            skipped.Add($"{key}: expected a number");
            return;
        }
        if (module == null)
        {
            skipped.Add($"{key}: this item has no {typeof(T).Name}");
            return;
        }
        try { set(module, v.GetSingle()); applied.Add($"{key}={v.GetSingle()}"); }
        catch (Exception ex) { skipped.Add($"{key}: {ex.Message}"); }
    }

    /// <summary>
    /// Sets plating on one instance.
    ///
    /// force=true attaches a plating module even when the item is not flagged
    /// _canBePlated. This is EXPERIMENTAL and expected to be of limited use: the
    /// game's own CheckForItemPlatingModule() consults that flag, and a forced module
    /// has a null PlatingUpgradeModifierData, so SetIsPlated may throw when it reaches
    /// for the damage multiplier. Even on success the visual comes from Refresh()
    /// swapping to a plated material the prefab may simply not have. Failures are
    /// caught and reported rather than thrown.
    /// </summary>
    public static bool TrySetPlated(ItemInstance instance, bool plated, out string error, bool force = false)
    {
        error = "";
        if (instance == null) { error = "no item instance"; return false; }
        try { instance.CheckForItemPlatingModule(); }
        catch (Exception ex) { error = $"CheckForItemPlatingModule threw: {ex.Message}"; return false; }

        var module = Find<ItemPlatingItemInstanceModule>(instance);

        if (module == null && force)
        {
            try
            {
                module = new ItemPlatingItemInstanceModule();
                instance._modules.Add(module);
            }
            catch (Exception ex)
            {
                error = $"forced module attach failed: {ex.Message}";
                return false;
            }
        }

        if (module == null)
        {
            error = "this item has no plating module (its ItemData is not flagged _canBePlated). Pass force:true to attempt it anyway.";
            return false;
        }

        try { module.SetIsPlated(plated); return true; }
        catch (Exception ex)
        {
            error = force
                ? $"SetIsPlated threw on a forced module (expected — its upgrade data is null): {ex.Message}"
                : $"SetIsPlated threw: {ex.Message}";
            return false;
        }
    }

    /// <summary>Builds a fresh instance with a module spec already applied.</summary>
    public static ItemInstance? CreateInstance(ItemData item, JsonElement? spec,
        List<string> applied, List<string> skipped, out string error)
    {
        error = "";
        ItemInstance instance;
        try { instance = new ItemInstance(item.Id); }
        catch (Exception ex) { error = $"could not create ItemInstance: {ex.Message}"; return null; }

        if (spec.HasValue && spec.Value.ValueKind == JsonValueKind.Object)
            Apply(instance, spec.Value, applied, skipped);

        return instance;
    }

    /// <summary>Convenience overload for the plating-only path.</summary>
    public static ItemInstance? CreateInstance(ItemData item, bool? plated, out string error)
    {
        error = "";
        ItemInstance instance;
        try { instance = new ItemInstance(item.Id); }
        catch (Exception ex) { error = $"could not create ItemInstance: {ex.Message}"; return null; }

        if (plated.HasValue && !TrySetPlated(instance, plated.Value, out error)) return null;
        return instance;
    }

    private static bool Safe(Func<bool> f) { try { return f(); } catch { return false; } }
    private static JsonNode? SafeF(Func<float> f)
    {
        try { var v = f(); return float.IsFinite(v) ? JsonValue.Create(v) : null; } catch { return null; }
    }
    private static JsonNode? SafeI(Func<int> f)
    {
        try { return JsonValue.Create(f()); } catch { return null; }
    }
}
