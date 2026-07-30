# Locates the Sons Of The Forest install. Every other script here dot-sources or
# calls this rather than carrying a hardcoded path, so the repo works on any machine.
#
# Order: $env:SOTF_GAME_DIR -> Steam's registry entry + libraryfolders.vdf -> common
# install roots on every fixed drive. Steps 1-2 are instant; step 3 probes a short
# list of paths per drive rather than walking the filesystem.
#
# Usage:
#   $GameDir = & "$PSScriptRoot\Find-GameDir.ps1"
#   $GameDir = & .\Find-GameDir.ps1 -Quiet     # $null instead of throwing
param([switch]$Quiet)

function Test-GameDir([string]$dir) {
    return $dir -and (Test-Path (Join-Path $dir "SonsOfTheForest.exe"))
}

$rel = "steamapps\common\Sons Of The Forest"

# 1. explicit override
if (Test-GameDir $env:SOTF_GAME_DIR) { return $env:SOTF_GAME_DIR }

# 2. Steam's own libraries
$steamRoots = @()
foreach ($k in @(
        @{ Path = "HKCU:\Software\Valve\Steam";             Name = "SteamPath" },
        @{ Path = "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam"; Name = "InstallPath" },
        @{ Path = "HKLM:\SOFTWARE\Valve\Steam";             Name = "InstallPath" })) {
    try {
        $v = (Get-ItemProperty -Path $k.Path -Name $k.Name -ErrorAction Stop).($k.Name)
        if ($v) { $steamRoots += ($v -replace '/', '\') }
    } catch { }
}

foreach ($steam in ($steamRoots | Select-Object -Unique)) {
    if (Test-GameDir (Join-Path $steam $rel)) { return (Join-Path $steam $rel) }

    # libraryfolders.vdf lists the extra libraries. Pulling the quoted "path" values
    # out is unambiguous and avoids writing a VDF parser for a single field.
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        $text = Get-Content $vdf -Raw
        foreach ($m in [regex]::Matches($text, '"path"\s*"([^"]+)"')) {
            $lib = $m.Groups[1].Value -replace '\\\\', '\'
            if (Test-GameDir (Join-Path $lib $rel)) { return (Join-Path $lib $rel) }
        }
    }
}

# 3. common roots, every ready drive
$candidates = @(
    $rel,
    "SteamLibrary\$rel",
    "Steam\$rel",
    "Games\$rel",
    "Program Files (x86)\Steam\$rel",
    "Program Files\Steam\$rel"
)
foreach ($drive in [System.IO.DriveInfo]::GetDrives()) {
    if (-not $drive.IsReady) { continue }
    if ($drive.DriveType -notin 'Fixed', 'Removable') { continue }
    foreach ($c in $candidates) {
        $p = Join-Path $drive.RootDirectory.FullName $c
        if (Test-GameDir $p) { return $p }
    }
}

if ($Quiet) { return $null }
throw "Could not find Sons Of The Forest. Set SOTF_GAME_DIR to the install folder, or pass -GameDir."
