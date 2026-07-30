# Dumps fields/properties/method signatures of a type from the RedLoader-generated
# IL2CPP interop assemblies, using the Mono.Cecil that ships with RedLoader.
# This is the evidence tool behind PLAN.md section 1 — rerun after any game patch.
#
# Usage:
#   .\dump-type.ps1 -Dll Sons.dll -TypeName TheForest.Items.Inventory.PlayerInventory
#   .\dump-type.ps1 -Dll Sons.Item.dll -TypeName Sons.Items.Core.ItemData -MethodFilter 'Add|Spawn'
param(
    [Parameter(Mandatory)][string]$Dll,
    [Parameter(Mandatory)][string]$TypeName,
    [string]$MethodFilter = '.',
    # 'Game' = generated game interop (Sons.dll, Sons.Item.dll, ...);
    # 'net6' = the loader/SDK itself (RedLoader.dll, SonsSdk.dll, ...)
    [ValidateSet('Game', 'net6')][string]$Folder = 'Game',
    [string]$GameDir = (& "$PSScriptRoot\Find-GameDir.ps1")
)
Add-Type -Path "$GameDir\_Redloader\net6\Mono.Cecil.dll" -ErrorAction SilentlyContinue
$m = [Mono.Cecil.ModuleDefinition]::ReadModule("$GameDir\_Redloader\$Folder\$Dll")
$t = $m.GetTypes() | Where-Object { $_.FullName -eq $TypeName }
if (-not $t) { Write-Host "TYPE NOT FOUND: $TypeName"; exit 1 }
Write-Host "=== $($t.FullName)  (base: $($t.BaseType.FullName)) ==="
Write-Host "--- FIELDS ---"
foreach ($f in $t.Fields) {
    if ($f.Name -notmatch 'NativeFieldInfoPtr|NativeMethodInfoPtr') {
        "{0} {1} {2}" -f ($f.IsStatic ? 'static' : '      '), $f.FieldType.Name, $f.Name
    }
}
Write-Host "--- PROPERTIES ---"
foreach ($p in $t.Properties) { "{0} {1}" -f $p.PropertyType.FullName, $p.Name }
Write-Host "--- METHODS ---"
foreach ($me in $t.Methods) {
    if ($me.Name -match $MethodFilter -and $me.Name -notmatch '^(get_|set_|add_|remove_)') {
        $ps = ($me.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
        "{0}{1} {2}({3})" -f ($me.IsStatic ? 'static ' : ''), $me.ReturnType.Name, $me.Name, $ps
    }
}
$m.Dispose()
