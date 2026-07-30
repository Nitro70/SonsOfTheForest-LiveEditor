# Builds the master cheatsheet from verified sources.
#
# SOURCES AND WHAT EACH PROVES
#   live autocomplete (console.introspect, 418)  - the game's own complete command
#                                                  registry. Authoritative for EXISTENCE.
#   Il2CppInterop proxies (359 `_` methods)      - authoritative for ARG TYPE.
#   Il2CppDumper DummyDll (359 `_` methods)      - independent confirmation of the above.
#   SonsSdk.dll [DebugCommand] attrs (16)        - commands added by RedLoader, not the game.
#   live ItemDatabaseManager (308 items)         - ground truth for items (they live in
#                                                  Unity assets, not code).
#
# NOTE: string-literal search does NOT work for `_` method commands - they are dispatched
# by reflection over method names, so the name never appears as a literal. Do not use
# literal absence as evidence of removal.
param(
    [string]$Repo = (Split-Path $PSScriptRoot -Parent),
    [string]$GameDir = (& "$PSScriptRoot\Find-GameDir.ps1"),
    [string]$BuildId = "20228174",
    # Optional: an older community list to diff against, so the "changed since" notes
    # can be regenerated. Skipped when not supplied.
    [string]$OldListFile
)

$refDir = Join-Path $Repo "reference"
Add-Type -Path "$GameDir\_Redloader\net6\Mono.Cecil.dll" -ErrorAction SilentlyContinue

# ---- 1. live registry (existence) -------------------------------------------------
$intro = Get-Content (Join-Path $refDir "console-introspect-raw.json") -Raw | ConvertFrom-Json
$autocomplete = $intro.result.autocomplete | ForEach-Object { ([string]$_).ToLower().TrimStart('_') } | Sort-Object -Unique

# ---- 2. arg types from the two decompiles ------------------------------------------
function Get-StaticCommands([string]$dll) {
    $m = [Mono.Cecil.ModuleDefinition]::ReadModule($dll)
    $t = $m.GetTypes() | Where-Object { $_.FullName -eq 'TheForest.DebugConsole' }
    $h = @{}
    foreach ($me in $t.Methods) {
        if ($me.Name -match '^_([a-zA-Z]\w*)$' -and $me.Parameters.Count -eq 1) {
            $h[$Matches[1].ToLower()] = $(if ($me.Parameters[0].ParameterType.FullName -eq 'System.String') { 'string' } else { 'none' })
        }
    }
    $m.Dispose(); return $h
}
$interop = Get-StaticCommands "$GameDir\_Redloader\Game\Sons.dll"
$dummy   = Get-StaticCommands (Join-Path $Repo "tools\decompile\out\DummyDll\Sons.dll")

# ---- 3. RedLoader-added commands ----------------------------------------------------
$sdkCmds = @{}
$rp = New-Object Mono.Cecil.ReaderParameters; $rp.ReadingMode = [Mono.Cecil.ReadingMode]::Immediate
$ms = [Mono.Cecil.ModuleDefinition]::ReadModule("$GameDir\_Redloader\net6\SonsSdk.dll", $rp)
foreach ($t in $ms.GetTypes()) {
    foreach ($me in $t.Methods) {
        foreach ($ca in $me.CustomAttributes) {
            if ($ca.AttributeType.Name -eq 'DebugCommandAttribute') { $sdkCmds[([string]$ca.ConstructorArguments[0].Value).ToLower()] = "$($t.Name).$($me.Name)" }
        }
    }
}
$ms.Dispose()

# ---- 4. old community list (what changed) -------------------------------------------
$old = @()
if ($OldListFile -and (Test-Path $OldListFile)) {
    $old = Get-Content $OldListFile |
        Where-Object { $_ -match '^([a-zA-Z][a-zA-Z0-9]+)\s{2,}(string|int|object|float)\s' } |
        ForEach-Object { ($_ -split '\s+')[0].ToLower() } | Sort-Object -Unique
}

# ---- 5. classify ---------------------------------------------------------------------
function Get-Category([string]$n) {
    switch -regex ($n) {
        '^(additem|removeitem|spawnitem|equipitem|addallitems|removeallitems|additemswithtag|setinventorypercent|clearpickups|gotopickup|spawnworldobject|refillcontainers)$' { 'Items & Inventory'; break }
        '^(addcharacter|addprefab|creepyvillage|creepyattackparty|robbycarry|removedead|removeliving)$' { 'Spawning & Characters'; break }
        '^(virginia|robby|timmy).*'                              { 'Companions & NPCs'; break }
        '^(godmode|invisible|superjump|speedyrun|energyhack|loghack|stonehack|buffstats|setstrengthlevel|gainstrength|heallocalplayer|regenhealth|killlocalplayer|revivelocalplayer|hitlocalplayer|knockdownlocalplayer|setplayerrace|instantrespawnhere|blockplayerfinaldeath|fakedrown|survival|crouchtoggle|sprinttoggle|setstat|addmemory|deathcount|playerkillstats|sleepcooldown)$' { 'Player Cheats & Stats'; break }
        '^(buildermode|instantbookbuild|cancelblueprints|finishblueprints|placestructure|enablestructureghosts|constructionrenderers.*|screwstructurerenderers.*|testautobuildeffigy|countlinkedstructures|damagefreeformstructure|destroyfreeformstructure)$' { 'Building & Structures'; break }
        '^(season|settimeofday|locktimeofday|jumptimeofday|setcurrentday|setgametimespeed|timescale|gravity|forcerain|unlockseason|regrowalltrees|treescutall|noforest|togglegrass|treeradius|forceremovetrees|spawnfallingtree|cloud.*|lightning.*|setwindintensity)$' { 'World, Time & Weather'; break }
        '^(goto|gotocoords|gotoforce|gototag|gotozone|xfreecam|freecamera|setlookrotation)$' { 'Teleport & Camera'; break }
        '^(save|load|saveplayer|loadplayer|getsaveid|setgamemode|getgamemode|setdifficultymode|setgamesetupsetting|setplayerdataflag|setfirsttimeseenitem|setexitedendgame)$' { 'Save, Load & Game Setup'; break }
        '^ai.*'                                                   { 'AI Debug'; break }
        '^(disconnectplayer|disconnectplayers|kickplayers|joinsteamlobby|dumplobbyinfo|netspawnplayer|localvoicedebugging|virtualplayers|netanimator|netskinnedbones|playernetanimator|sendmessageto|golfcartnetworkdebug|networktransformlodupdate)$' { 'Multiplayer'; break }
        '^(killradius|damageradius|gibradius|shockradius|igniteradius|burnbodyradius|dismemberradius|clearbushradius|breakobjects|slapchop|destroy|destroywildcard|destroyragdoll|skunksmell)$' { 'Destruction & Area Effects'; break }
        '^(show|toggle|log|lod|mipmap|terrain|greeble|dag|mesh|material|shader|render|diag|debug|dump|count|list|find|inspect|profiler|anim|audio|color|fog|exposure|camera|dynamicres|quality|billboard|aniso|areashadow|astar|physics|targetframerate|samplefps|worldbounds|vitalsshow|checkfrozen|checkattached|refreshentities|listactiveentities|enablecollisionbasedkillbox|iswaterdisplacementenabled|oceancollider|newfogrendering|uiforce|resetheldanim|resetinputaxes|skinnedmeshes|createlight|showactivelights|spawnedobjectstats|treefallcontactinfo|treecutsimulatebolt|treeocclusionbonus|treemapdistributionfilter|worldobject|restoreallworldlocators|setworldobjectstaterange|hideworldposfor|showworldposfor|duplicateobject|enable|disable|unload|clear|gccollect|reset|report|rumbletest|playdeath|playgameover|playcutscene|playrecording|recordplayer|saverecording|flyovertrigger|demomode|testeventmask|allowasync|workscheduler|toggleworkscheduler|worldgroupid|wsscaling|setspeakermode|setproperty|setsetting|help|cheats|capsulemode|diggingclear|doorclose|dooropen|autodriveraft|playerstriggertraps|playerinterruptkeys|firstlookforce|itemgroupinteractionaudio|inventoryspeedsensitivity|invertlook|mouse|gamepad|animalsenabled|animallimitmult|showbutterflyinfo|cavelight|characterlods|setlayerculldistance|getlayerculldistance|countgowithlayer|listgowithlayer|togglego|gotodag|exportlinkedstructurestojson|importlinkedstructuresfromfile|outputsnappointstofile|screwstructure|superstructure|grabberdebug|firedebug|zones|steamtimeline|achievement|setopeningcrash|setterrainaniso|loaddebugconsolemod|loadmacros|openmacrosfolder|saveconsolepos|timeofday|virtualpickupdebug|userigidbodyrotation|gameoverdelaytime|checkexitmenu|clearmidactionflag|clearallsettings|resetsettings|togglevsync|toggleocclusionculling|togglefpsdisplay|toggleoverlay|toggleplayerstats|postprocessingcomponent|qualitytexture|replaceshader|removeshader|findobjectswithshader|applydefault).*' { 'Debug & Rendering'; break }
        default { 'Other' }
    }
}

$rows = foreach ($n in $autocomplete) {
    $isStatic = $interop.ContainsKey($n)
    $isSdk = $sdkCmds.ContainsKey($n)
    $args = if ($isStatic) { $interop[$n] } else { 'string' }
    [pscustomobject]@{
        name     = $n
        args     = $args
        source   = if ($isSdk) { 'RedLoader (mod)' } elseif ($isStatic) { 'Base game (static)' } else { 'Base game (dynamic)' }
        argProof = if ($isStatic) { if ($interop[$n] -eq $dummy[$n]) { 'verified x2' } else { 'MISMATCH' } } else { 'inferred' }
        category = Get-Category $n
        inOldList= ($n -in $old)
    }
}

$removed = $old | Where-Object { $_ -notin $autocomplete }
$newToUser = $autocomplete | Where-Object { $_ -notin $old }

# ---- 6. items -------------------------------------------------------------------------
$itemsDoc = Get-Content (Join-Path $refDir "items-dump-raw.json") -Raw | ConvertFrom-Json
$items = $itemsDoc.result.items | Sort-Object id

# ---- 7. write -------------------------------------------------------------------------
$stamp = Get-Date -Format "yyyy-MM-dd"
$sb = [System.Text.StringBuilder]::new()
function W($s = "") { [void]$sb.AppendLine($s) }

W "SONS OF THE FOREST - MASTER CHEATSHEET"
W ("=" * 100)
W "Generated : $stamp"
W "Build     : Steam buildid $BuildId (Unity 2022.2.16f1, IL2CPP)"
W "Items     : $($items.Count)      Commands: $($rows.Count)"
W ""
W "HOW THIS WAS VERIFIED"
W "  Command existence : the live console's own registry (its autocomplete table)."
W "  Command arguments : two independent decompiles (Il2CppInterop proxies + Il2CppDumper)."
W "  Item data         : the live ItemDatabaseManager. Items are Unity assets, not code,"
W "                      so the running game is more authoritative than any decompile."
W ""
W "SOURCE COLUMN"
W "  Base game (static)  - a _method on DebugConsole. Arg type verified twice."
W "  Base game (dynamic) - registered at runtime by game code. Arg type inferred (all"
W "                        RegisterCommand handlers take a string)."
W "  RedLoader (mod)     - ONLY EXISTS because RedLoader is installed. Absent in vanilla."
W ("=" * 100)
W ""

W "### REMOVED FROM THIS BUILD ($($removed.Count)) - in old community lists, gone now"
W ("-" * 100)
W "These are absent from the console's registry. Calling them does nothing."
W ""
foreach ($r in ($removed | Sort-Object)) {
    $note = switch ($r) {
        'buildhack' { "  <- functionality revived by LiveEditor as 'restored.buildhack'" }
        'ammohack'  { "  <- no surviving machinery found" }
        default     { "" }
    }
    W ("  {0,-24}{1}" -f $r, $note)
}
W ""

W "### WORKING COMMANDS BY CATEGORY ($($rows.Count))"
W ("=" * 100)
foreach ($cat in ($rows | Group-Object category | Sort-Object Name)) {
    W ""
    W "## $($cat.Name)  ($($cat.Count))"
    W ("-" * 100)
    W ("  {0,-34} {1,-8} {2,-22} {3}" -f "COMMAND", "ARGS", "SOURCE", "ARG PROOF")
    foreach ($c in ($cat.Group | Sort-Object name)) {
        W ("  {0,-34} {1,-8} {2,-22} {3}" -f $c.name, $c.args, $c.source, $c.argProof)
    }
}

W ""
W ("=" * 100)
W "### ADDED BY REDLOADER (not in vanilla) ($(($rows | Where-Object source -eq 'RedLoader (mod)').Count))"
W ("-" * 100)
foreach ($c in ($rows | Where-Object source -eq 'RedLoader (mod)' | Sort-Object name)) { W ("  {0,-26} {1}" -f $c.name, $sdkCmds[$c.name]) }

W ""
W ("=" * 100)
W "### ITEMS ($($items.Count))"
W ("-" * 100)
W "Display Name = what the game shows. Internal = engine id (console may want this)."
W "Max = stack ceiling. Spawn = has a world pickup prefab."
W ""
W ("  {0,-6} {1,-34} {2,-30} {3,-6} {4,-6} {5}" -f "ID", "DISPLAY NAME", "INTERNAL", "MAX", "SPAWN", "FLAGS")
W ("  " + ("-" * 96))
foreach ($i in $items) {
    $flags = @()
    if ($i.eachInstanceIsUnique) { $flags += "unique" }
    if ($i.isLegacyItem) { $flags += "legacy" }
    if ($i.canNotBeStored) { $flags += "no-store" }
    if (-not $i.hasPickupPrefab) { $flags += "no-prefab" }
    W ("  {0,-6} {1,-34} {2,-30} {3,-6} {4,-6} {5}" -f $i.id, $i.displayName, $i.internalName, $i.maxAmount, $(if ($i.canBeSpawned) { "yes" } else { "no" }), ($flags -join ","))
}

W ""
W ("=" * 100)
W "### ITEMS BY TYPE"
W ("-" * 100)
foreach ($grp in ($items | Group-Object type | Sort-Object Name)) {
    W ""
    W "## $($grp.Name)  ($($grp.Count))"
    foreach ($i in ($grp.Group | Sort-Object displayName)) { W ("  {0,-6} {1}" -f $i.id, $i.displayName) }
}

$sb.ToString() | Set-Content (Join-Path $refDir "CHEATSHEET.txt") -Encoding UTF8

[pscustomobject]@{
    generated = $stamp; gameBuildId = $BuildId
    commandCount = $rows.Count; itemCount = $items.Count
    commands = $rows; removedFromBuild = $removed; items = $items
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $refDir "cheatsheet.json") -Encoding UTF8

"CHEATSHEET.txt / cheatsheet.json -> $refDir"
"commands=$($rows.Count)  items=$($items.Count)  removed=$($removed.Count)"
"by source: static=$(($rows|Where-Object source -eq 'Base game (static)').Count)  dynamic=$(($rows|Where-Object source -eq 'Base game (dynamic)').Count)  redloader=$(($rows|Where-Object source -eq 'RedLoader (mod)').Count)"
$mismatch = $rows | Where-Object argProof -eq 'MISMATCH'
"arg-type mismatches between the two decompiles: $($mismatch.Count)"
