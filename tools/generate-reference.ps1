# Regenerates SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt in the repo root from
# reference\cheatsheet.json.
#
# RULE: no invented descriptions. A command only gets a description if it was either
# (a) executed and observed against a running game, or (b) read out of real managed
# source (SonsSdk). Everything else gets its verified name/arg-type/usage form and
# nothing more, because a plausible guess is worse than an honest blank.
param(
    [string]$Repo = (Split-Path $PSScriptRoot -Parent),
    [string]$Target,
    [string]$BuildId = "20228174"
)
if (-not $Target) { $Target = Join-Path $Repo "SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt" }

$refDir = Join-Path $Repo "reference"
$cheat = Get-Content (Join-Path $refDir "cheatsheet.json") -Raw | ConvertFrom-Json
$commands = $cheat.commands
$removed = $cheat.removedFromBuild
$items = $cheat.items

# Display-name repair: the game returns "???" for artifact pieces (deliberate in-game
# mystery text) and blank for a few others. Fall back to a humanized internal name so
# the sheet stays usable.
function Fix-Name([string]$display, [string]$internal) {
    if ([string]::IsNullOrWhiteSpace($display) -or $display -match '^\?+$') {
        $s = $internal -replace '_', ' '
        $s = [regex]::Replace($s, '(?<=[a-z0-9])(?=[A-Z])', ' ')
        return $s.Trim()
    }
    return $display
}

# Descriptions with a verifiable basis only.
$verified = @{
    # executed and observed this session
    'godmode'        = @{ usage = 'godmode on|off';           desc = 'Invincibility. VERIFIED: a felled tree dealt no damage with it on.' }
    'showfps'        = @{ usage = 'showfps on|off';           desc = 'Toggles the FPS overlay. VERIFIED: executed both ways.' }
    'spawnitem'      = @{ usage = 'spawnitem <id|name>';      desc = 'Spawns the item on the ground near you. VERIFIED. Refuses items whose canBeSpawned flag is false (see the NOT SPAWNABLE list).' }
    'additem'        = @{ usage = 'additem <id|name>';        desc = 'Puts the item in your inventory. VERIFIED. Refused when a slot is occupied or the item cannot be carried right now.' }
    'equipitem'      = @{ usage = 'equipitem <id|name>';      desc = 'Equips the item directly. VERIFIED working.' }
    'removeitem'     = @{ usage = 'removeitem <id|name>';     desc = 'Removes one of the item from your inventory.' }
    'stonehack'      = @{ usage = 'stonehack on|off';         desc = 'Unlimited stone. Confirmed working by the user.' }
    # read out of real SonsSdk managed source
    'dump'           = @{ usage = 'dump items|characters|prefabs'; desc = 'Writes a .txt listing to disk. The items listing includes a Spawnable flag per entry. SOURCE-VERIFIED (SonsSdk).' }
    'noforest'       = @{ usage = 'noforest';                 desc = 'Removes Trees, Bushes and SmallTree objects. SOURCE-VERIFIED (SonsSdk).' }
    'gotopickup'     = @{ usage = 'gotopickup <name>';        desc = 'Finds a matching world pickup and teleports you to it. SOURCE-VERIFIED (SonsSdk).' }
    'playcutscene'   = @{ usage = 'playcutscene <name>';      desc = 'Plays a named cutscene; reports "Couldn''t play cutscene" on a bad name. SOURCE-VERIFIED (SonsSdk).' }
    'clearpickups'   = @{ usage = 'clearpickups';             desc = 'Removes dropped/spawned world pickups. SOURCE-VERIFIED (SonsSdk). Useful after bulk spawning.' }
    'xfreecam'       = @{ usage = 'xfreecam';                 desc = 'RedLoader''s free camera. SOURCE-VERIFIED (SonsSdk).' }
    'togglegrass'    = @{ usage = 'togglegrass';              desc = 'Toggles grass rendering. SOURCE-VERIFIED (SonsSdk).' }
    'cancelblueprints' = @{ usage = 'cancelblueprints';       desc = 'Cancels all placed blueprints. SOURCE-VERIFIED (SonsSdk).' }
    'finishblueprints' = @{ usage = 'finishblueprints';       desc = 'Instantly completes all placed blueprints. SOURCE-VERIFIED (SonsSdk).' }
    'getsaveid'      = @{ usage = 'getsaveid';                desc = 'Prints the current save id. SOURCE-VERIFIED (SonsSdk).' }
    'saveconsolepos' = @{ usage = 'saveconsolepos';           desc = 'Saves the console window position. SOURCE-VERIFIED (SonsSdk).' }
    'placestructure' = @{ usage = 'placestructure <name>';    desc = 'Places a structure. SOURCE-VERIFIED (SonsSdk).' }
    'aighostplayer'  = @{ usage = 'aighostplayer on|off';     desc = 'AI ignores you. SOURCE-VERIFIED (SonsSdk) - note this is added by RedLoader, not vanilla.' }
    'virginiasentiment' = @{ usage = 'virginiasentiment <value>'; desc = 'Sets Virginia''s sentiment. SOURCE-VERIFIED (SonsSdk).' }
    'virginiavisit'  = @{ usage = 'virginiavisit <value>';    desc = 'Triggers a Virginia visit. SOURCE-VERIFIED (SonsSdk).' }
    'dumpboltserializers' = @{ usage = 'dumpboltserializers'; desc = 'Dumps network serializers. SOURCE-VERIFIED (SonsSdk).' }
    'addprefab'      = @{ usage = 'addprefab <name>  (e.g. addprefab golfcart)'; desc = 'Spawns a PREFAB, not an item. This is the only way to get a golf cart - it is not in the item database, so spawnitem cannot produce one.' }
    'addcharacter'   = @{ usage = 'addcharacter <name> [count]'; desc = 'Spawns creatures/NPCs. Registered dynamically, which is why older static command dumps miss it.' }
}

$stamp = Get-Date -Format "yyyy-MM-dd"
$sb = [System.Text.StringBuilder]::new()
function W($s = "") { [void]$sb.AppendLine($s) }

W "SONS OF THE FOREST - ITEM IDs & CONSOLE COMMANDS"
W ("=" * 100)
W "Generated   : $stamp"
W "Game build  : Steam buildid $BuildId  -  Unity 2022.2.16f1, IL2CPP"
W "Totals      : $($commands.Count) working commands, $($items.Count) items"
W ""
W "WHERE THIS DATA COMES FROM"
W "  Commands exist  : read from the live console's own command registry in a running game."
W "  Command args    : cross-checked against TWO independent decompiles of the game"
W "                    (Il2CppInterop proxies and an Il2CppDumper dump). 0 disagreements."
W "  Items           : read from the live ItemDatabaseManager. Item definitions live in"
W "                    Unity assets rather than code, so the running game is the only"
W "                    authoritative source - no decompile can give you this."
W ""
W "ABOUT THE DESCRIPTIONS - READ THIS"
W "  Only commands that were actually executed and observed, or whose real source code"
W "  was read, carry a description. Everything else deliberately has NO description."
W "  Widely-circulated community lists describe roughly 100 commands that nobody appears"
W "  to have verified (or pad them with filler like 'argument meaning not documented"
W "  publicly'). Those guesses are omitted here rather than repeated. A blank means"
W "  'not verified', not 'does nothing'."
W ""
W ("=" * 100)
W ""
W ""
W ("#" * 100)
W "#  PART 1 - ITEM IDs"
W ("#" * 100)
W ""
W "IMPORTANT - 'SPAWNABLE' MEANS TWO DIFFERENT THINGS"
W ""
W "  Every one of the $($items.Count) items below has a world pickup prefab, so ALL of them can be"
W "  placed in the world by mod tooling that calls UnityEngine Instantiate directly."
W "  VERIFIED: all $($items.Count) were spawned this way in a single sweep, 0 failures."
W ""
W "  The  spawnitem  console command is stricter - it obeys each item's internal"
W "  canBeSpawned flag, and 60 items have that flag set to false. That is the only"
W "  difference between the two lists below."
W ""
W "  Worked example: the 8 artifact items (707, 662, 667, 668, 669, 689, 708, 712) all"
W "  have canBeSpawned=false, so  spawnitem  refuses them - yet all 8 were spawned"
W "  successfully by mod tooling and confirmed on screen by their lightning effect."
W ""
W "  Vehicles are a separate system again: the golf cart is NOT an item and does not"
W "  appear anywhere in this list. Use  addprefab golfcart."
W ""

$spawnable = $items | Where-Object { $_.canBeSpawned } | Sort-Object id
$notSpawnable = $items | Where-Object { -not $_.canBeSpawned } | Sort-Object id

W ("=" * 100)
W "GROUP A - WORKS WITH spawnitem ($($spawnable.Count))"
W ("=" * 100)
W "Also placeable by mod tooling."
W ""
W ("  {0,-6} {1,-36} {2,-30} {3,-5} {4}" -f "ID", "NAME", "INTERNAL NAME", "MAX", "FLAGS")
W ("  " + ("-" * 96))
foreach ($i in $spawnable) {
    $flags = @()
    if ($i.eachInstanceIsUnique) { $flags += "unique" }
    if ($i.isLegacyItem) { $flags += "legacy" }
    if ($i.canNotBeStored) { $flags += "no-store" }
    W ("  {0,-6} {1,-36} {2,-30} {3,-5} {4}" -f $i.id, (Fix-Name $i.displayName $i.internalName), $i.internalName, $i.maxAmount, ($flags -join ","))
}

W ""
W ("=" * 100)
W "GROUP B - BLOCKED FOR spawnitem, BUT MOD TOOLING CAN STILL PLACE THEM ($($notSpawnable.Count))"
W ("=" * 100)
W "canBeSpawned=false, so the  spawnitem  console command refuses these. They are NOT"
W "unspawnable in general: all $($notSpawnable.Count) were placed successfully by mod tooling (VERIFIED)."
W "Mostly story notes, blueprints and artifact pieces. additem may also work on some."
W ""
W ("  {0,-6} {1,-36} {2,-30} {3,-5} {4}" -f "ID", "NAME", "INTERNAL NAME", "MAX", "FLAGS")
W ("  " + ("-" * 96))
foreach ($i in $notSpawnable) {
    $flags = @()
    if ($i.eachInstanceIsUnique) { $flags += "unique" }
    if ($i.isLegacyItem) { $flags += "legacy" }
    if ($i.canNotBeStored) { $flags += "no-store" }
    if (-not $i.hasPickupPrefab) { $flags += "no-prefab" }
    W ("  {0,-6} {1,-36} {2,-30} {3,-5} {4}" -f $i.id, (Fix-Name $i.displayName $i.internalName), $i.internalName, $i.maxAmount, ($flags -join ","))
}

W ""
W ""
W ("#" * 100)
W "#  PART 2 - CONSOLE COMMANDS"
W ("#" * 100)
W ""
W "OPENING THE CONSOLE"
W "  With RedLoader installed : cheats are enabled automatically. Press F1."
W "  Vanilla game             : type  cheatstick  while in-world (nothing appears to"
W "                             happen), then press F1."
W "  Multiplayer              : the host controls cheats via Options > Gameplay >"
W "                             Allow Cheats. Clients follow the host's setting."
W ""
W "ARGUMENT COLUMN"
W "  string  - takes a value or a toggle, e.g.  godmode on"
W "  none    - takes no argument, just run it"
W ""
W ("=" * 100)
W "WORKING COMMANDS ($($commands.Count)) - alphabetical"
W ("=" * 100)
W ""

foreach ($c in ($commands | Sort-Object name)) {
    $v = $verified[$c.name]
    $usage = if ($v) { $v.usage } elseif ($c.args -eq 'string') { "$($c.name) <value>" } else { $c.name }
    $tag = switch ($c.source) {
        'RedLoader (mod)'     { "  [RedLoader-only]" }
        'Base game (dynamic)' { "  [arg type inferred]" }
        default               { "" }
    }
    W ("{0}" -f $c.name)
    W ("    args  : {0}{1}" -f $c.args, $tag)
    W ("    usage : {0}" -f $usage)
    if ($v) { W ("    does  : {0}" -f $v.desc) }
    W ""
}

W ""
W ("=" * 100)
W "HIDDEN COMMANDS (0)"
W ("=" * 100)
W "None. This was checked directly rather than assumed: the console keeps two separate"
W "tables - every command it can dispatch, and the subset its autocomplete offers while"
W "you type. Those were compared inside a running game after forcing the autocomplete"
W "table to build. Every dispatchable command appears in autocomplete, so the game hides"
W "nothing from you. (The autocomplete table is in fact LARGER, because it also lists"
W "commands registered at runtime that are not plain methods.)"
W ""
W ""
W ("=" * 100)
W "REMOVED COMMANDS ($($removed.Count)) - present in older community lists, gone from this build"
W ("=" * 100)
W "These are absent from the live console registry. Typing them does nothing."
W ""
foreach ($r in ($removed | Sort-Object)) {
    $note = switch ($r) {
        'buildhack' { " - the command is gone, but the underlying InfiniteItemHack/InstantBuild properties still exist on StructureCraftingSystem and can be driven by a mod." }
        'ammohack'  { " - no surviving machinery found." }
        'spawnpickup' { " - superseded by spawnitem." }
        default     { "" }
    }
    W ("  {0}{1}" -f $r, $note)
}

W ""
W ("=" * 100)
W "HOW THIS DIFFERS FROM THE COMMONLY-CIRCULATED LISTS"
W ("=" * 100)
W "  - Item count is $($items.Count), not 352. The widely-copied lists carry debug/unrealized"
W "    ids that do not exist in this build."
W "  - Item names here are the game's own display names. Several commonly-listed names"
W "    are wrong: id 78 is 'Log' (internal Log_Legacy), id 355 is 'Pistol' not"
W "    'Compact Pistol', id 431 is 'Firefighter Axe' not 'Fire Axe'."
W "  - Stack ceilings are included (ammo caps at 1000, logs at 2). Most lists omit them."
W "  - $($commands.Count) commands are listed, against the usual ~314. Lists built by scanning for"
W "    _<name> methods miss every command registered at runtime - including addcharacter."
W "  - 16 of the listed commands come from RedLoader and DO NOT EXIST in the vanilla game."
W "    They are tagged [RedLoader-only]."
W "  - Unverified descriptions and filler text are omitted rather than repeated."

$sb.ToString() | Set-Content $Target -Encoding UTF8
"wrote -> $Target"
"lines: $((Get-Content $Target | Measure-Object -Line).Lines)   size: $([math]::Round((Get-Item $Target).Length/1KB)) KB"
"commands: $($commands.Count)  spawnable: $($spawnable.Count)  notSpawnable: $($notSpawnable.Count)"
