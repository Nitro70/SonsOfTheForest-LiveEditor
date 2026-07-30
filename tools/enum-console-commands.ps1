# Enumerates the built-in console commands of the CURRENT game build (the
# `_<command>` methods on TheForest.DebugConsole) and, optionally, diffs them
# against the reference txt's fixed-width command table.
# This regenerates the PLAN.md section 1.10 stale/missing lists after any patch.
#
# Usage:
#   .\enum-console-commands.ps1
#   .\enum-console-commands.ps1 -RefFile ..\SOTF_ITEM_IDS_AND_CONSOLE_COMMANDS.txt
param(
    [string]$RefFile,
    [string]$GameDir = (& "$PSScriptRoot\Find-GameDir.ps1")
)
Add-Type -Path "$GameDir\_Redloader\net6\Mono.Cecil.dll" -ErrorAction SilentlyContinue
$m = [Mono.Cecil.ModuleDefinition]::ReadModule("$GameDir\_Redloader\Game\Sons.dll")
$t = $m.GetTypes() | Where-Object { $_.FullName -eq 'TheForest.DebugConsole' }
$live = $t.Methods | Where-Object { $_.Name -match '^_[a-zA-Z]' } |
    ForEach-Object { $_.Name.Substring(1).ToLower() } | Sort-Object -Unique
$m.Dispose()
Write-Host "live built-in console commands: $($live.Count)"
$live

if ($RefFile) {
    $tableCmds = Get-Content $RefFile |
        Where-Object { $_ -match '^([a-zA-Z][a-zA-Z0-9]+)\s{2,}(string|int|object|float)\s' } |
        ForEach-Object { ($_ -split '\s+')[0].ToLower() } | Sort-Object -Unique
    $stale = $tableCmds | Where-Object { $_ -notin $live }
    $missing = $live | Where-Object { $_ -notin $tableCmds }
    Write-Host "`nIN FILE BUT NOT IN LIVE BUILD ($($stale.Count)):"; $stale -join ', '
    Write-Host "`nIN LIVE BUILD BUT NOT IN FILE ($($missing.Count)):"; $missing -join ', '
}
