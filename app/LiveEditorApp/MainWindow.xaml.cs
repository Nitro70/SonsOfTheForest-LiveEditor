using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LiveEditorApp;

public sealed class ItemRow
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string InternalName { get; init; } = "";
    public int MaxAmount { get; init; }
    public bool CanBeSpawned { get; init; }
    public bool CanBePlated { get; init; }
    public string Type { get; init; } = "";
    public string SpawnableText => CanBeSpawned ? "yes" : "blocked";
    public string PlatableText => CanBePlated ? "yes" : "";
}

public sealed class CmdRow
{
    public string Name { get; init; } = "";
    public string Args { get; init; } = "";
    public bool Hidden { get; init; }
    public string HiddenText => Hidden ? "hidden" : "";
}

public sealed class StatRow
{
    public string Name { get; init; } = "";
    public float Current { get; init; }
    public float Max { get; init; }
    public float Min { get; init; }
    public float Regen { get; init; }
    public float Fade { get; init; }
    public bool Writable { get; init; } = true;

    /// <summary>Health only: the ceiling natural regeneration heals up to.</summary>
    public float? TargetHealth { get; init; }

    /// <summary>Cheap text bar so fill level is readable at a glance.</summary>
    public string Bar
    {
        get
        {
            if (Max <= Min) return "";
            var pct = Math.Clamp((Current - Min) / (Max - Min), 0f, 1f);
            var filled = (int)Math.Round(pct * 20);
            return new string('█', filled) + new string('·', 20 - filled) + $"  {pct * 100:0}%";
        }
    }

    /// <summary>Why a stat may not stay where you put it.</summary>
    public string Notes
    {
        get
        {
            var parts = new List<string>();
            if (!Writable) parts.Add("read-only");
            if (TargetHealth is { } t) parts.Add($"regen target {t:0.#}");
            if (Regen > 0) parts.Add($"regen {Regen:0.##}/s");
            if (Fade > 0) parts.Add($"fade {Fade:0.##}/s");
            return string.Join("  ·  ", parts);
        }
    }
}

public partial class MainWindow : Window
{
    // Located on first use; the port/token are read from UserData\LiveEditor.port
    // beneath it. Cached because the answer cannot change while the app is running.
    private string? _gameDir;

    private readonly LiveEditorClient _client = new();
    private readonly List<ItemRow> _allItems = new();
    private readonly List<CmdRow> _allCommands = new();

    public MainWindow()
    {
        InitializeComponent();

        _client.OnLog += msg => Dispatcher.Invoke(() => Log($"[client] {msg}"));
        _client.OnConnectionChanged += connected => Dispatcher.Invoke(() => SetConnected(connected));
        _client.OnPushEvent += (name, data) => Dispatcher.Invoke(() =>
        {
            Log($"[event] {name} {data?.ToJsonString() ?? ""}");
            if (name is "world.entered" or "world.exited" or "session.changed") _ = RefreshStateAsync();
        });

        Loaded += async (_, _) => await TryConnectAsync();
    }

    // ---------- connection ----------

    private async Task TryConnectAsync()
    {
        _gameDir ??= await Task.Run(GameLocator.Find);
        if (_gameDir is null)
        {
            Status($"Could not find Sons Of The Forest. Put its folder path in {GameLocator.OverrideFilePath}");
            Log($"[client] game folder not found; create {GameLocator.OverrideFilePath} containing the install path");
            return;
        }

        Status("Connecting…");
        Log($"[client] game folder: {_gameDir}");
        var ok = await _client.ConnectAsync(_gameDir);
        Status(ok ? "Connected." : "Could not connect — is the game running with the mod loaded?");
        if (ok)
        {
            await RefreshStateAsync();
            await LoadItemsAsync();
            await LoadCommandsAsync();
        }
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e) => await TryConnectAsync();

    private void SetConnected(bool connected)
    {
        StatusDot.Fill = connected
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("Danger");
        StatusText.Text = connected ? "Connected" : "Disconnected";
    }

    private async void OnRefreshStateClick(object sender, RoutedEventArgs e) => await RefreshStateAsync();

    private async Task RefreshStateAsync()
    {
        var r = await _client.SendAsync("state.get");
        if (!r.Ok) { Status($"state.get failed: {r.Describe()}"); return; }

        WorldText.Text = (r.Result?["inWorld"]?.GetValue<bool>() ?? false) ? "in world" : "menu";
        ModeText.Text = r.Result?["mode"]?.GetValue<string>() ?? "—";
        CheatsText.Text = (r.Result?["cheatsEnabled"]?.GetValue<bool>() ?? false) ? "enabled" : "off";
    }

    // ---------- items ----------

    private async void OnLoadItemsClick(object sender, RoutedEventArgs e) => await LoadItemsAsync();

    private async Task LoadItemsAsync()
    {
        Status("Loading items…");
        var r = await _client.SendAsync("item.list", timeoutMs: 30000);
        if (!r.Ok) { Status($"item.list failed: {r.Describe()}"); return; }

        _allItems.Clear();
        if (r.Result?["items"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                if (n is null) continue;
                _allItems.Add(new ItemRow
                {
                    Id = n["id"]?.GetValue<int>() ?? 0,
                    Name = n["name"]?.GetValue<string>() ?? "",
                    InternalName = n["internalName"]?.GetValue<string>() ?? "",
                    MaxAmount = n["maxAmount"]?.GetValue<int>() ?? 0,
                    CanBeSpawned = n["canBeSpawned"]?.GetValue<bool>() ?? false,
                    CanBePlated = n["canBePlated"]?.GetValue<bool>() ?? false,
                    Type = n["type"]?.GetValue<string>() ?? "",
                });
            }
        }

        ApplyItemFilter();
        Status($"Loaded {_allItems.Count} items.");
    }

    private void OnItemSearchChanged(object sender, TextChangedEventArgs e) => ApplyItemFilter();

    private void ApplyItemFilter()
    {
        var q = ItemSearch.Text?.Trim() ?? "";

        // Always project a NEW list. Assigning _allItems directly when the filter is
        // empty hands the grid back the same reference it already holds, and List<T>
        // raises no change notification — so a reload (e.g. after spawn.unlock) left
        // the grid showing stale rows while the count label updated. Self-contradicting.
        var view = _allItems.Where(i =>
            string.IsNullOrEmpty(q) ||
            i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            i.InternalName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            i.Id.ToString() == q).ToList();

        ItemGrid.ItemsSource = view;
        ItemCountText.Text = $"{view.Count} shown / {_allItems.Count} total";
    }

    /// <summary>First row of the selection — for operations that only make sense on one item.</summary>
    private ItemRow? SelectedItem => SelectedItems.FirstOrDefault();

    /// <summary>
    /// The whole selection, in the grid's display order rather than click order, so a
    /// shift-click range is processed top-to-bottom as the user sees it.
    /// </summary>
    private List<ItemRow> SelectedItems
    {
        get
        {
            var selected = ItemGrid.SelectedItems.OfType<ItemRow>().ToHashSet();
            if (selected.Count == 0) return new List<ItemRow>();
            return ItemGrid.Items.OfType<ItemRow>().Where(selected.Contains).ToList();
        }
    }

    private int Qty => int.TryParse(QtyBox.Text, out var q) && q > 0 ? q : 1;

    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var n = ItemGrid.SelectedItems.Count;
        ModuleItemText.Text = n switch
        {
            0 => "No item selected",
            1 => $"{SelectedItem?.Name}  (id {SelectedItem?.Id})",
            _ => $"{n} items selected",
        };

        // Load the item's real module state into the toggles. Since every toggle is
        // now sent on Apply, they have to start out matching the item, otherwise an
        // untouched box would silently switch something off.
        if (n == 1 && _client.IsConnected) _ = LoadModulesIntoToggles(SelectedItem!, quiet: true);
    }

    /// <summary>
    /// Sends one command per selected row and returns every response for the caller
    /// to interpret. Deliberately does NOT decide what "success" means: item.add
    /// returns ok=true with added=0 when the inventory is already at cap, so treating
    /// a protocol-level ok as an effect would report "succeeded" while nothing
    /// actually changed. Requests are pipelined by the plugin, so a large selection
    /// still resolves in roughly one batch.
    /// </summary>
    private async Task<List<(ItemRow row, CommandResponse resp)>> SendForSelectionAsync(
        string cmd, Func<ItemRow, JsonObject> argsFor)
    {
        var rows = SelectedItems;
        var tasks = rows.Select(row => (row, task: _client.SendAsync(cmd, argsFor(row)))).ToList();

        var results = new List<(ItemRow, CommandResponse)>(tasks.Count);
        foreach (var (row, task) in tasks) results.Add((row, await task));
        return results;
    }

    private static int IntOf(CommandResponse r, string key) =>
        r.Result?[key]?.GetValue<int>() ?? 0;

    private static bool BoolOf(CommandResponse r, string key) =>
        r.Result?[key]?.GetValue<bool>() ?? false;

    /// <summary>
    /// Reports what actually happened: a total effect count, plus how many items were
    /// no-ops and why. "0 added because everything is at cap" is a materially
    /// different outcome from "0 added because the command failed".
    /// </summary>
    private void ReportEffect(string action, string unit,
        List<(ItemRow row, CommandResponse resp)> results,
        Func<CommandResponse, int> effect,
        Func<CommandResponse, string?>? noOpReason = null)
    {
        var total = 0;
        var changed = 0;
        var noOp = 0;
        var failed = 0;
        string? firstProblem = null;
        var reasons = new List<string>();

        foreach (var (row, r) in results)
        {
            if (!r.Ok)
            {
                failed++;
                firstProblem ??= $"{row.Name}: {r.Describe()}";
                Log($"  {row.Name}: FAILED {r.Describe()}");
                continue;
            }

            var n = effect(r);
            total += n;
            if (n > 0)
            {
                changed++;
                Log($"  {row.Name}: {n} {unit}");
            }
            else
            {
                noOp++;
                var why = noOpReason?.Invoke(r) ?? "no change";
                reasons.Add(why);
                Log($"  {row.Name}: 0 {unit} ({why})");
            }
        }

        var parts = new List<string> { $"{action}: {total} {unit} across {changed} item(s)" };
        if (noOp > 0)
        {
            var common = reasons.GroupBy(x => x).OrderByDescending(g => g.Count()).First();
            parts.Add($"{noOp} unchanged ({common.Key})");
        }
        if (failed > 0) parts.Add($"{failed} failed — {firstProblem}");

        Status(string.Join("; ", parts));
    }

    private void ReportBatch(string action, int ok, int failed, string? firstError)
    {
        if (failed == 0) Status($"{action}: {ok} item(s) succeeded.");
        else if (ok == 0) Status($"{action}: all {failed} failed — {firstError}");
        else Status($"{action}: {ok} ok, {failed} failed — first: {firstError}");
    }

    private bool WantPlated => PlatedBox.IsChecked == true;

    private async void OnAddItemClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedItems;
        if (rows.Count == 0) { Status("Select one or more items first."); return; }

        Log($"item.add x{Qty}{(WantPlated ? " plated" : "")} for {rows.Count} item(s)");
        var results = await SendForSelectionAsync("item.add", row =>
        {
            var a = new JsonObject { ["item"] = row.Id, ["qty"] = Qty };
            if (WantPlated) a["plated"] = true;
            return a;
        });

        ReportEffect("Add", "added", results,
            r => IntOf(r, "added"),
            r => BoolOf(r, "capped") ? "already at max"
               : BoolOf(r, "refused") ? "game refused (slot occupied or not carryable now)"
               : "no change");
    }

    private async void OnPlateItemClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedItems;
        if (rows.Count == 0) { Status("Select one or more items first."); return; }

        // Filter to platable rather than firing doomed requests: with a big selection
        // the failures would drown the useful result.
        var platable = rows.Where(r => r.CanBePlated).ToList();
        if (platable.Count == 0)
        {
            Status($"None of the {rows.Count} selected item(s) can be plated.");
            return;
        }

        var ok = 0; var failed = 0; string? firstError = null;
        foreach (var row in platable)
        {
            var r = await _client.SendAsync("item.plate",
                new JsonObject { ["item"] = row.Id, ["plated"] = true, ["all"] = true });
            if (r.Ok) ok++;
            else { failed++; firstError ??= $"{row.Name}: {r.Describe()}"; }
        }

        var skippedNote = rows.Count > platable.Count ? $" ({rows.Count - platable.Count} not platable, skipped)" : "";
        ReportBatch("Plate" + skippedNote, ok, failed, firstError);
    }

    private async void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedItems;
        if (rows.Count == 0) { Status("Select one or more items first."); return; }

        Log($"item.remove x{Qty} for {rows.Count} item(s)");
        var results = await SendForSelectionAsync("item.remove",
            row => new JsonObject { ["item"] = row.Id, ["qty"] = Qty });

        ReportEffect("Remove", "removed", results,
            r => IntOf(r, "removed"),
            _ => "you don't own any");
    }

    private async void OnSpawnItemClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedItems;
        if (rows.Count == 0) { Status("Select one or more items first."); return; }

        var at = (SpawnAtBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "look";
        var strategy = (StrategyBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "auto";

        Log($"item.spawn x{Qty} at {at} for {rows.Count} item(s)");
        var results = await SendForSelectionAsync("item.spawn", row =>
        {
            var a = new JsonObject { ["item"] = row.Id, ["qty"] = Qty, ["at"] = at };
            if (strategy != "auto") a["strategy"] = strategy;
            if (WantPlated) a["plated"] = true;
            return a;
        });

        ReportEffect("Spawn", "spawned", results, r => IntOf(r, "spawned"));
    }

    private async void OnReconClick(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null) { Status("Select an item first."); return; }
        var r = await _client.SendAsync("recon.prefab", new JsonObject { ["item"] = SelectedItem.Id });
        Log($"recon.prefab {SelectedItem.Id} -> {r.Describe()}");
        Status(r.Ok ? $"bolt-registered: {r.Result?["boltRegistered"]}, strategy: {r.Result?["chosenStrategy"]}" : r.Describe());
    }

    // ---------- console ----------

    private async void OnLoadCommandsClick(object sender, RoutedEventArgs e) => await LoadCommandsAsync();

    private async Task LoadCommandsAsync()
    {
        Status("Loading commands…");
        var r = await _client.SendAsync("console.list", timeoutMs: 30000);
        if (!r.Ok) { Status($"console.list failed: {r.Describe()}"); return; }

        _allCommands.Clear();
        if (r.Result?["builtin"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                if (n is null) continue;
                _allCommands.Add(new CmdRow
                {
                    Name = n["name"]?.GetValue<string>() ?? "",
                    Args = n["args"]?.GetValue<string>() ?? "",
                    Hidden = n["hidden"]?.GetValue<bool>() ?? false,
                });
            }
        }
        if (r.Result?["dynamic"] is JsonArray dyn)
        {
            foreach (var n in dyn)
            {
                if (n is null) continue;
                _allCommands.Add(new CmdRow { Name = n.GetValue<string>(), Args = "string", Hidden = false });
            }
        }

        ApplyCmdFilter();
        Status($"Loaded {_allCommands.Count} commands.");
    }

    private void OnCmdSearchChanged(object sender, TextChangedEventArgs e) => ApplyCmdFilter();

    private void ApplyCmdFilter()
    {
        var q = CmdSearch.Text?.Trim() ?? "";
        // Same fresh-list rule as ApplyItemFilter — see the note there.
        var view = _allCommands
            .Where(c => string.IsNullOrEmpty(q) || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();

        CmdGrid.ItemsSource = view;
        CmdCountText.Text = $"{view.Count} shown / {_allCommands.Count} total";
    }

    private void OnCmdSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CmdGrid.SelectedItem is CmdRow row)
            CmdLine.Text = row.Args == "none" ? row.Name : row.Name + " ";
    }

    private async void OnCmdLineKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RunConsoleLineAsync();
    }

    private async void OnRunCommandClick(object sender, RoutedEventArgs e) => await RunConsoleLineAsync();

    private async Task RunConsoleLineAsync()
    {
        var line = CmdLine.Text?.Trim();
        if (string.IsNullOrEmpty(line)) return;

        var r = await _client.SendAsync("console.exec", new JsonObject { ["line"] = line });
        Log($"console.exec \"{line}\" -> {r.Describe()}");
        ShowCommandOutput(line, r);
        Status(r.Ok ? $"Ran {r.Result?["command"]} ({r.Result?["dispatch"]})." : r.Describe());
    }

    /// <summary>Renders whatever the console printed while the command ran.</summary>
    private void ShowCommandOutput(string line, CommandResponse r)
    {
        CmdOutput.AppendText($"$ {line}{Environment.NewLine}");

        if (!r.Ok)
        {
            CmdOutput.AppendText($"  !! {r.Describe()}{Environment.NewLine}");
        }
        else if (r.Result?["output"] is JsonArray outArr && outArr.Count > 0)
        {
            foreach (var l in outArr)
                CmdOutput.AppendText($"  {l?.GetValue<string>()}{Environment.NewLine}");
        }
        else
        {
            CmdOutput.AppendText($"  (ran, no console output captured){Environment.NewLine}");
        }

        CmdOutput.ScrollToEnd();
    }

    private async void OnQuickCheat(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string line) return;
        var r = await _client.SendAsync("console.exec", new JsonObject { ["line"] = line });
        Log($"console.exec \"{line}\" -> {r.Describe()}");
        Status(r.Ok ? $"Ran {line}." : r.Describe());
    }

    // ---------- item modules ----------

    /// <summary>
    /// The boolean toggles are plain on/off and every one of them is sent, so the
    /// panel says exactly what the item ends up with — a ticked box is on, an empty
    /// box is off, and there is no third state to misread.
    ///
    /// The safety that the old indeterminate state provided now comes from loading an
    /// item's real values into the toggles when it is selected, so a box the user
    /// never touches already holds the item's current setting and applying it is a
    /// no-op. Keys whose module the item does not have come back as "skipped".
    ///
    /// The numeric fields stay opt-in (blank = don't send): unlike a checkbox, a
    /// blank textbox has no sensible "off" value to fall back to.
    /// </summary>
    private JsonObject BuildModuleSpec()
    {
        var spec = new JsonObject();

        foreach (var t in BooleanToggles) spec[t.Key] = t.Box.IsChecked == true;

        void Int(string key, TextBox tb)
        {
            if (int.TryParse(tb.Text?.Trim(), out var v)) spec[key] = v;
        }
        Int("ammoCount", ModAmmoCount);
        Int("ammoType", ModAmmoType);
        Int("variant", ModVariant);

        if (float.TryParse(ModFill.Text?.Trim(), out var fill)) spec["fill"] = fill;

        return spec;
    }

    /// <summary>Module key -> its toggle, and where that key's value lives in a read.</summary>
    private (string Key, CheckBox Box, string Module, string Field)[] BooleanToggles => new[]
    {
        ("plating",            ModPlating,      "plating",         "plated"),
        ("pauseDecay",         ModPauseDecay,   "perishable",      "pauseDecay"),
        ("arrowUpgradeActive", ModArrowUpgrade, "arrowUpgrade",    "active"),
        ("gpsActive",          ModGpsActive,    "gpsLocator",      "isActive"),
        ("gpsPulse",           ModGpsPulse,     "gpsLocator",      "pulseIcon"),
        ("gpsBeep",            ModGpsBeep,      "gpsLocator",      "beepInRange"),
        ("limbFresh",          ModLimbFresh,    "dismemberedLimb", "isFresh"),
    };

    private async void OnReadModulesClick(object sender, RoutedEventArgs e)
    {
        if (SelectedItem is null) { Status("Select an item on the Items tab first."); return; }
        await LoadModulesIntoToggles(SelectedItem, quiet: false);
    }

    /// <summary>
    /// Reads an item's live module state and mirrors it into the toggles, so what is
    /// on screen is what the item currently is.
    /// </summary>
    private async Task LoadModulesIntoToggles(ItemRow item, bool quiet)
    {
        ModuleItemText.Text = $"{item.Name}  (id {item.Id})";

        var r = await _client.SendAsync("item.modules", new JsonObject { ["item"] = item.Id });

        // A not-owned item is the normal case while browsing the catalogue, so on the
        // automatic path it must not shout — but the toggles still have to be cleared,
        // otherwise the previous item's settings would be applied to this one.
        if (!r.Ok)
        {
            SetToggles(null);
            ModuleReadout.Text = r.Describe();
            if (!quiet) Status(r.Describe());
            return;
        }

        var modules = r.Result?["modules"] as JsonObject;
        SetToggles(modules);

        ModuleReadout.Text = modules?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
                             ?? "(no modules on this item)";
        if (!quiet) Status($"Read modules for {item.Name}.");
    }

    /// <summary>Null clears everything to off.</summary>
    private void SetToggles(JsonObject? modules)
    {
        foreach (var (_, box, module, field) in BooleanToggles)
        {
            var value = modules?[module]?[field];
            box.IsChecked = value?.GetValue<bool>() ?? false;
        }
    }

    private async void OnApplyModulesClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedItems;
        if (rows.Count == 0) { Status("Select one or more items on the Items tab first."); return; }

        var spec = BuildModuleSpec();

        // Single selection keeps the detailed applied/skipped readout; a multi
        // selection reports a batch summary, since one readout cannot show N items.
        if (rows.Count == 1)
        {
            var r = await _client.SendAsync("item.modules", new JsonObject
            {
                ["item"] = rows[0].Id,
                ["all"] = ModuleAllBox.IsChecked == true,
                ["set"] = spec,
            });
            Log($"item.modules set {rows[0].Id} -> {r.Describe()}");
            ReportModuleResult(r);
            return;
        }

        Log($"item.modules set for {rows.Count} item(s)");
        var results = await SendForSelectionAsync("item.modules", row => new JsonObject
        {
            ["item"] = row.Id,
            ["all"] = ModuleAllBox.IsChecked == true,
            ["set"] = JsonNode.Parse(spec.ToJsonString())!, // detach: a node has one parent
        });

        // "applied" counts keys that actually took, so a module the item lacks shows
        // up as unchanged rather than as a success.
        ReportEffect("Modules", "keys applied", results,
            r => (r.Result?["applied"] as JsonArray)?.Count ?? 0,
            r =>
            {
                var skipped = r.Result?["skipped"] as JsonArray;
                return skipped is { Count: > 0 }
                    ? skipped[0]?.GetValue<string>() ?? "all keys skipped"
                    : "no keys applied";
            });

        var effective = results.Count(x => x.resp.Ok && ((x.resp.Result?["applied"] as JsonArray)?.Count ?? 0) > 0);
        ModuleReadout.Text = $"Applied to {effective} of {results.Count} selected item(s).\r\nSee the Log tab for per-item detail.";
    }

    private async void OnAddWithModulesClick(object sender, RoutedEventArgs e)
    {
        var rows = SelectedItems;
        if (rows.Count == 0) { Status("Select one or more items on the Items tab first."); return; }

        var spec = BuildModuleSpec();

        if (rows.Count == 1)
        {
            var r = await _client.SendAsync("item.add", new JsonObject
            {
                ["item"] = rows[0].Id,
                ["qty"] = Qty,
                ["modules"] = spec,
            });
            Log($"item.add with modules {rows[0].Id} -> {r.Describe()}");
            ReportModuleResult(r);
            return;
        }

        Log($"item.add with modules for {rows.Count} item(s)");
        var results = await SendForSelectionAsync("item.add", row => new JsonObject
        {
            ["item"] = row.Id,
            ["qty"] = Qty,
            ["modules"] = JsonNode.Parse(spec.ToJsonString())!,
        });

        ReportEffect("Add with modules", "added", results,
            r => IntOf(r, "added"),
            r => BoolOf(r, "capped") ? "already at max"
               : BoolOf(r, "refused") ? "game refused" : "no change");
    }

    /// <summary>Surfaces applied/skipped explicitly — a skipped key is the useful signal.</summary>
    private void ReportModuleResult(CommandResponse r)
    {
        if (!r.Ok) { ModuleReadout.Text = r.Describe(); Status(r.Describe()); return; }

        var sb = new System.Text.StringBuilder();
        foreach (var key in new[] { "applied", "modulesApplied" })
        {
            if (r.Result?[key] is JsonArray arr && arr.Count > 0)
                sb.AppendLine("APPLIED: " + string.Join(", ", arr.Select(x => x?.GetValue<string>())));
        }
        foreach (var key in new[] { "skipped", "modulesSkipped" })
        {
            if (r.Result?[key] is JsonArray arr && arr.Count > 0)
                foreach (var s in arr) sb.AppendLine("SKIPPED: " + s?.GetValue<string>());
        }
        if (r.Result?["modules"] is JsonNode mods)
            sb.AppendLine().AppendLine(mods.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        ModuleReadout.Text = sb.Length > 0 ? sb.ToString() : r.Describe();
        Status("Done — check APPLIED/SKIPPED above.");
    }

    private void OnClearModulesClick(object sender, RoutedEventArgs e)
    {
        SetToggles(null);
        foreach (var tb in new[] { ModAmmoCount, ModAmmoType, ModVariant, ModFill })
            tb.Text = "";
        Status("Inputs cleared — all toggles off.");
    }

    // ---------- player ----------

    private async void OnPlayerGetClick(object sender, RoutedEventArgs e) => await LoadPlayerStatsAsync();

    private async Task LoadPlayerStatsAsync()
    {
        var r = await _client.SendAsync("player.get");
        if (!r.Ok) { Status($"player.get failed: {r.Describe()}"); return; }

        var rows = new List<StatRow>();
        if (r.Result?["stats"] is JsonObject stats)
        {
            foreach (var kv in stats)
            {
                if (kv.Value is not JsonObject o) continue;
                if (o["current"] is null) continue; // stat unavailable
                rows.Add(new StatRow
                {
                    Name = kv.Key,
                    Current = o["current"]?.GetValue<float>() ?? 0,
                    Max = o["max"]?.GetValue<float>() ?? 0,
                    Min = o["min"]?.GetValue<float>() ?? 0,
                    Regen = o["regenRate"]?.GetValue<float>() ?? 0,
                    Fade = o["fadeRate"]?.GetValue<float>() ?? 0,
                    Writable = o["writable"]?.GetValue<bool>() ?? true,
                    TargetHealth = o["targetHealth"]?.GetValue<float>(),
                });
            }
        }

        StatGrid.ItemsSource = rows;
        Status($"Read {rows.Count} stats.");
    }

    private string SelectedStat =>
        (StatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "health";

    private async void OnPlayerSetClick(object sender, RoutedEventArgs e)
    {
        if (!float.TryParse(StatValue.Text, out var val)) { Status("Enter a number."); return; }
        var r = await _client.SendAsync("player.set",
            new JsonObject { ["stat"] = SelectedStat, ["value"] = val });
        Log($"player.set {SelectedStat}={val} -> {r.Describe()}");
        ReportStatSet(SelectedStat, r);
    }

    private async void OnPlayerMaxClick(object sender, RoutedEventArgs e)
    {
        var r = await _client.SendAsync("player.set",
            new JsonObject { ["stat"] = SelectedStat, ["max"] = true });
        Log($"player.set {SelectedStat}=max -> {r.Describe()}");
        ReportStatSet(SelectedStat, r);
    }

    /// <summary>
    /// Reports what the game actually did, not that the command returned ok. A write
    /// the game silently clamps or regenerates away must not read as success.
    /// </summary>
    private async void ReportStatSet(string stat, CommandResponse r)
    {
        if (!r.Ok) { Status(r.Describe()); return; }

        var before = r.Result?["before"]?.GetValue<float>();
        var after = r.Result?["after"]?.GetValue<float>();
        var changed = r.Result?["changed"]?.GetValue<bool>() ?? false;
        var note = r.Result?["note"]?.GetValue<string>();

        var msg = changed
            ? $"{stat}: {before:0.##} -> {after:0.##}"
            : $"{stat}: unchanged (still {after:0.##})";
        if (!string.IsNullOrEmpty(note)) msg += $" — {note}";

        Status(msg);
        await LoadPlayerStatsAsync();
    }

    private async void OnPlayerRestoreClick(object sender, RoutedEventArgs e)
    {
        var r = await _client.SendAsync("player.restore");
        Log($"player.restore -> {r.Describe()}");
        Status(r.Ok ? "Restored." : r.Describe());
        if (r.Ok) await LoadPlayerStatsAsync();
    }

    // ---------- tools ----------

    private async void OnSpawnUnlockClick(object sender, RoutedEventArgs e)
    {
        var r = await _client.SendAsync("spawn.unlock");
        Log($"spawn.unlock -> {r.Describe()}");
        Status(r.Ok ? $"Unlocked {r.Result?["unlocked"]} items for spawnitem." : r.Describe());
        if (r.Ok) await LoadItemsAsync();
    }

    private async void OnSpawnRestoreClick(object sender, RoutedEventArgs e)
    {
        var r = await _client.SendAsync("spawn.restore");
        Log($"spawn.restore -> {r.Describe()}");
        Status(r.Ok ? $"Restored {r.Result?["restored"]} flags." : r.Describe());
        if (r.Ok) await LoadItemsAsync();
    }

    private async void OnBuildhackOnClick(object sender, RoutedEventArgs e) => await BuildhackAsync(true);
    private async void OnBuildhackOffClick(object sender, RoutedEventArgs e) => await BuildhackAsync(false);

    private async Task BuildhackAsync(bool on)
    {
        var r = await _client.SendAsync("restored.buildhack", new JsonObject { ["enabled"] = on });
        Log($"restored.buildhack {on} -> {r.Describe()}");
        Status(r.Ok ? $"buildhack infiniteItems={r.Result?["infiniteItems"]}" : r.Describe());
    }

    private async void OnInstantBuildOnClick(object sender, RoutedEventArgs e) => await InstantBuildAsync(true);
    private async void OnInstantBuildOffClick(object sender, RoutedEventArgs e) => await InstantBuildAsync(false);

    private async Task InstantBuildAsync(bool on)
    {
        var r = await _client.SendAsync("restored.instantbuild", new JsonObject { ["enabled"] = on });
        Log($"restored.instantbuild {on} -> {r.Describe()}");
        Status(r.Ok ? $"instantBuild={r.Result?["instantBuild"]}" : r.Describe());
    }

    private async void OnConsoleUnlockClick(object sender, RoutedEventArgs e)
    {
        var r = await _client.SendAsync("console.unlock");
        Log($"console.unlock -> {r.Describe()}");
        Status(r.Ok ? "Console/cheats unlocked." : r.Describe());
    }

    private async void OnIntrospectClick(object sender, RoutedEventArgs e)
    {
        var r = await _client.SendAsync("console.introspect", timeoutMs: 30000);
        if (!r.Ok) { Status(r.Describe()); Log($"console.introspect -> {r.Describe()}"); return; }

        var avail = r.Result?["availableCount"]?.GetValue<int>() ?? 0;
        var auto = r.Result?["autocompleteCount"]?.GetValue<int>() ?? 0;
        var hidden = r.Result?["hiddenCount"]?.GetValue<int>() ?? 0;
        Log($"console.introspect -> dispatchable={avail} autocomplete={auto} hidden={hidden}");
        Status(auto == 0
            ? "Autocomplete list empty — open the in-game console once (F1), then retry."
            : $"dispatchable={avail}, autocomplete={auto}, hidden={hidden}");
        if (r.Ok) await LoadCommandsAsync();
    }

    // ---------- misc ----------

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogBox.Clear();

    private const int MaxLogChars = 200_000;

    private void Log(string msg)
    {
        // Bound both the per-line length and the total: item.list logs its entire
        // result JSON, which is hundreds of KB and makes the tab unusable otherwise.
        if (msg.Length > 2000) msg = msg[..2000] + $"… (+{msg.Length - 2000} chars)";

        LogBox.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
        if (LogBox.Text.Length > MaxLogChars)
            LogBox.Text = LogBox.Text[^(MaxLogChars / 2)..];

        LogBox.ScrollToEnd();
    }

    private void Status(string msg) => StatusBar.Text = msg;
}
