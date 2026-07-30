# Generates the item reference from a live `dump.items` capture (reference/items-dump-raw.json).
# Ground truth: item definitions live in Unity assets, so the running game's
# ItemDatabaseManager is more authoritative than any decompile of GameAssembly.dll.
param(
    [string]$Raw = (Join-Path (Split-Path $PSScriptRoot -Parent) "reference\items-dump-raw.json"),
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "reference"),
    [string]$BuildId = "20228174",
    [string]$GameVersion = "Unity 2022.2.16f1"
)

$doc = Get-Content $Raw -Raw | ConvertFrom-Json
$items = $doc.result.items | Sort-Object id
$stamp = (Get-Item $Raw).LastWriteTime.ToString("yyyy-MM-dd")

# --- clean JSON for the external app ---
$jsonOut = Join-Path $OutDir "items.json"
[pscustomobject]@{
    generated   = $stamp
    source      = "live ItemDatabaseManager via LiveEditor dump.items"
    gameBuildId = $BuildId
    engine      = $GameVersion
    count       = $items.Count
    items       = $items
} | ConvertTo-Json -Depth 6 | Set-Content $jsonOut -Encoding UTF8

# --- human-readable reference ---
$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine("SONS OF THE FOREST - ITEM REFERENCE (generated from the live game)")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine("Generated : $stamp")
$null = $sb.AppendLine("Source    : live ItemDatabaseManager (ground truth, not a community list)")
$null = $sb.AppendLine("Build     : Steam buildid $BuildId / $GameVersion")
$null = $sb.AppendLine("Total     : $($items.Count) items")
$null = $sb.AppendLine("")
$null = $sb.AppendLine("Display Name = what the game calls it on screen (use this in the app/UI).")
$null = $sb.AppendLine("Internal     = engine identifier; the console sometimes wants this form.")
$null = $sb.AppendLine("Max          = stack ceiling.  Spawn = has a world pickup prefab.")
$null = $sb.AppendLine("Unique       = each instance is tracked separately (weapons/tools).")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine("")
$null = $sb.AppendLine(("{0,-5}  {1,-34} {2,-30} {3,-5} {4,-6} {5}" -f "ID", "DISPLAY NAME", "INTERNAL", "MAX", "SPAWN", "FLAGS"))
$null = $sb.AppendLine("-" * 110)

foreach ($i in $items) {
    $flags = @()
    if ($i.eachInstanceIsUnique) { $flags += "unique" }
    if ($i.isLegacyItem)         { $flags += "legacy" }
    if ($i.canNotBeStored)       { $flags += "no-store" }
    if (-not $i.hasPickupPrefab) { $flags += "no-prefab" }
    if ($i.tags -and $i.tags.Count) { $flags += "tags:" + ($i.tags -join "/") }

    $null = $sb.AppendLine(("{0,-5}  {1,-34} {2,-30} {3,-5} {4,-6} {5}" -f `
        $i.id, $i.displayName, $i.internalName, $i.maxAmount, `
        $(if ($i.canBeSpawned) { "yes" } else { "no" }), ($flags -join ", ")))
}

$null = $sb.AppendLine("")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine("BY TYPE")
$null = $sb.AppendLine("=" * 78)
foreach ($grp in ($items | Group-Object type | Sort-Object Name)) {
    $null = $sb.AppendLine("")
    $null = $sb.AppendLine("[$($grp.Name)]  ($($grp.Count))")
    foreach ($i in ($grp.Group | Sort-Object displayName)) {
        $null = $sb.AppendLine(("  {0,-5} {1}" -f $i.id, $i.displayName))
    }
}

$txtOut = Join-Path $OutDir "items-reference.txt"
$sb.ToString() | Set-Content $txtOut -Encoding UTF8

"items.json          -> $jsonOut"
"items-reference.txt -> $txtOut"
"total items: $($items.Count)"
