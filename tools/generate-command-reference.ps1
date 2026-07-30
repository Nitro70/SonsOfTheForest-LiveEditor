# Generates the console-command reference by cross-checking two independent sources:
#   1. Il2CppInterop proxy assemblies (_Redloader\Game) - what the plugin dispatches through
#   2. Il2CppDumper DummyDll output  (tools\decompile\out\DummyDll) - independent decompile
# Only commands confirmed by BOTH are marked verified.
param(
    [string]$GameDir = (& "$PSScriptRoot\Find-GameDir.ps1"),
    [string]$DummyDir = (Join-Path $PSScriptRoot "decompile\out\DummyDll"),
    [string]$OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "reference"),
    # Optional prior list to diff against; skipped when not supplied.
    [string]$RefFile,
    [string]$BuildId = "20228174"
)

Add-Type -Path "$GameDir\_Redloader\net6\Mono.Cecil.dll" -ErrorAction SilentlyContinue

function Get-Commands([string]$dllPath) {
    $m = [Mono.Cecil.ModuleDefinition]::ReadModule($dllPath)
    $t = $m.GetTypes() | Where-Object { $_.FullName -eq 'TheForest.DebugConsole' }
    $res = @{}
    foreach ($me in $t.Methods) {
        if ($me.Name -match '^_([a-zA-Z]\w*)$' -and $me.Parameters.Count -eq 1) {
            $name = $Matches[1]
            $pt = $me.Parameters[0].ParameterType.FullName
            $res[$name] = $(if ($pt -eq 'System.String') { 'string' } else { 'none' })
        }
    }
    $m.Dispose()
    return $res
}

$interop = Get-Commands "$GameDir\_Redloader\Game\Sons.dll"
$decomp = Get-Commands "$DummyDir\Sons.dll"

$all = ($interop.Keys + $decomp.Keys) | Sort-Object -Unique
$rows = foreach ($n in $all) {
    [pscustomobject]@{
        name       = $n
        args       = $(if ($interop.ContainsKey($n)) { $interop[$n] } else { $decomp[$n] })
        inInterop  = $interop.ContainsKey($n)
        inDecompile= $decomp.ContainsKey($n)
        verified   = ($interop.ContainsKey($n) -and $decomp.ContainsKey($n))
    }
}

# Compare against the old community reference file, if present
$oldList = @()
if (Test-Path $RefFile) {
    $oldList = Get-Content $RefFile |
        Where-Object { $_ -match '^([a-zA-Z][a-zA-Z0-9]+)\s{2,}(string|int|object|float)\s' } |
        ForEach-Object { ($_ -split '\s+')[0].ToLower() } | Sort-Object -Unique
}
$live = $rows | ForEach-Object { $_.name.ToLower() }
$goneFromBuild = $oldList | Where-Object { $_ -notin $live }
$newToYou      = $live | Where-Object { $_ -notin $oldList }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format "yyyy-MM-dd"

[pscustomobject]@{
    generated       = $stamp
    gameBuildId     = $BuildId
    sources         = @("Il2CppInterop proxies", "Il2CppDumper DummyDll")
    total           = $rows.Count
    verifiedByBoth  = ($rows | Where-Object verified).Count
    commands        = $rows
    absentFromBuild = $goneFromBuild
    undocumentedInOldFile = $newToYou
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir "console-commands.json") -Encoding UTF8

$sb = [System.Text.StringBuilder]::new()
$null = $sb.AppendLine("SONS OF THE FOREST - CONSOLE COMMAND REFERENCE (generated)")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine("Generated : $stamp")
$null = $sb.AppendLine("Build     : Steam buildid $BuildId")
$null = $sb.AppendLine("Sources   : Il2CppInterop proxies AND an independent Il2CppDumper decompile")
$null = $sb.AppendLine("Total     : $($rows.Count) built-in commands ($(($rows | Where-Object verified).Count) confirmed by both sources)")
$null = $sb.AppendLine("")
$null = $sb.AppendLine("args=string -> command takes a value/toggle, e.g.  godmode on")
$null = $sb.AppendLine("args=none   -> command takes no argument, just run it")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine("")
foreach ($r in ($rows | Sort-Object name)) {
    $null = $sb.AppendLine(("{0,-34} {1,-7} {2}" -f $r.name, $r.args, $(if ($r.verified) { "" } else { "(UNCONFIRMED: only in $(if($r.inInterop){'interop'}else{'decompile'}))" })))
}
$null = $sb.AppendLine("")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine("IN YOUR OLD LIST BUT NOT IN THIS BUILD ($($goneFromBuild.Count)) - these are gone:")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine(($goneFromBuild -join ", "))
$null = $sb.AppendLine("")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine("IN THIS BUILD BUT MISSING FROM YOUR OLD LIST ($($newToYou.Count)):")
$null = $sb.AppendLine("=" * 78)
$null = $sb.AppendLine(($newToYou -join ", "))

$sb.ToString() | Set-Content (Join-Path $OutDir "console-commands.txt") -Encoding UTF8

"console-commands.json / .txt -> $OutDir"
"total=$($rows.Count)  verifiedByBoth=$(($rows | Where-Object verified).Count)  goneFromBuild=$($goneFromBuild.Count)  newToYou=$($newToYou.Count)"
