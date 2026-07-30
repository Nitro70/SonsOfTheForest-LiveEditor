@echo off & powershell -NoProfile -ExecutionPolicy Bypass -Command "$Self='%~f0'; $s=[IO.File]::ReadAllText($Self); Invoke-Expression ($s.Substring($s.IndexOf([char]10)+1))" & exit /b
# ============================================================================
#  Sons Of The Forest - Live Editor : installer
#
#  Finds the game, installs RedLoader if it is missing, drops the mod into
#  Mods\, installs the control app and makes a Desktop shortcut. No prompts.
#
#  This file is a batch/PowerShell polyglot. cmd executes line 1, which re-reads
#  the file and evaluates everything AFTER the first newline as PowerShell — so
#  line 1 is the only line that has to be valid batch, and every line below it
#  is ordinary PowerShell.
# ============================================================================

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

# $Self is set by the batch line above. Invoke-Expression runs with no script file
# of its own, so $MyInvocation.MyCommand.Path is null here — the path has to come
# from cmd. The fallback covers running this file directly as a .ps1.
if (-not $Self) { $Self = $MyInvocation.MyCommand.Path }
$Root         = Split-Path -Parent $Self
$RedLoaderTag = '0.8.6'   # pinned fallback if the GitHub API is unreachable
$Failed       = $false

function Say  ($m) { Write-Host $m }
function Step ($m) { Write-Host "  $m" -ForegroundColor Gray }
function Good ($m) { Write-Host "  [ok] $m" -ForegroundColor Green }
function Warn ($m) { Write-Host "  [!]  $m" -ForegroundColor Yellow }
function Fail ($m) { Write-Host "  [X]  $m" -ForegroundColor Red; $script:Failed = $true }

Say ""
Say "  Sons Of The Forest - Live Editor"
Say "  ================================"
Say ""

# ---------------------------------------------------------------- find game
function Test-GameDir([string]$d) { $d -and (Test-Path (Join-Path $d 'SonsOfTheForest.exe')) }

function Find-Game {
    $rel = 'steamapps\common\Sons Of The Forest'

    if (Test-GameDir $env:SOTF_GAME_DIR) { return $env:SOTF_GAME_DIR }

    # Steam's own libraries: registry root, then every path in libraryfolders.vdf.
    $roots = @()
    foreach ($k in @(
            @{ P = 'HKCU:\Software\Valve\Steam';             N = 'SteamPath' },
            @{ P = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; N = 'InstallPath' },
            @{ P = 'HKLM:\SOFTWARE\Valve\Steam';             N = 'InstallPath' })) {
        try {
            $v = (Get-ItemProperty -Path $k.P -Name $k.N -ErrorAction Stop).($k.N)
            if ($v) { $roots += ($v -replace '/', '\') }
        } catch { }
    }
    foreach ($steam in ($roots | Select-Object -Unique)) {
        $p = Join-Path $steam $rel
        if (Test-GameDir $p) { return $p }
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]+)"')) {
                $p = Join-Path ($m.Groups[1].Value -replace '\\\\', '\') $rel
                if (Test-GameDir $p) { return $p }
            }
        }
    }

    # Not a Steam-registered copy: probe the usual roots on every ready drive.
    # Probing a fixed list beats walking the filesystem - this stays instant.
    $cands = @($rel, "SteamLibrary\$rel", "Steam\$rel", "Games\$rel",
               "Program Files (x86)\Steam\$rel", "Program Files\Steam\$rel")
    foreach ($drv in [IO.DriveInfo]::GetDrives()) {
        if (-not $drv.IsReady) { continue }
        if ($drv.DriveType -notin 'Fixed', 'Removable') { continue }
        foreach ($c in $cands) {
            $p = Join-Path $drv.RootDirectory.FullName $c
            if (Test-GameDir $p) { return $p }
        }
    }
    return $null
}

Step "Looking for Sons Of The Forest..."
$sw = [Diagnostics.Stopwatch]::StartNew()
$Game = Find-Game
$sw.Stop()

if (-not $Game) {
    Fail "Could not find Sons Of The Forest on any drive."
    Say ""
    Say "  Set SOTF_GAME_DIR to the install folder and run this again, e.g.:"
    Say "      setx SOTF_GAME_DIR ""D:\Games\Sons Of The Forest"""
    Say ""
    Start-Sleep -Seconds 20
    exit 1
}
Good "$Game  ($([int]$sw.ElapsedMilliseconds) ms)"

if (Get-Process -Name 'SonsOfTheForest' -ErrorAction SilentlyContinue) {
    Fail "The game is running. Close it and run this installer again."
    Start-Sleep -Seconds 15
    exit 1
}

# ----------------------------------------------------------------- RedLoader
# Left alone when already present: a working loader install is not worth
# clobbering, and the mod only needs the loader to exist.
if (Test-Path (Join-Path $Game '_Redloader\net6\RedLoader.dll')) {
    Good "RedLoader already installed"
} else {
    Step "Downloading RedLoader..."
    $url = "https://github.com/ToniMacaroni/RedLoader/releases/download/$RedLoaderTag/Redloader.zip"
    try {
        $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/ToniMacaroni/RedLoader/releases/latest' `
                                 -Headers @{ 'User-Agent' = 'SOTF-LiveEditor-Installer' } -TimeoutSec 20
        $asset = $rel.assets | Where-Object { $_.name -match '\.zip$' } | Select-Object -First 1
        if ($asset) { $url = $asset.browser_download_url }
    } catch {
        Warn "GitHub API unavailable, using pinned $RedLoaderTag"
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("sotf-redloader-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    $zip = Join-Path $tmp 'Redloader.zip'
    try {
        (New-Object Net.WebClient).DownloadFile($url, $zip)
        Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $tmp 'x') -Force
        # Copy rather than extract straight in: the game folder already has files
        # and Expand-Archive will not overwrite into a populated tree.
        Copy-Item (Join-Path $tmp 'x\*') -Destination $Game -Recurse -Force
        Good "RedLoader installed"
    } catch {
        Fail "RedLoader install failed: $($_.Exception.Message)"
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ----------------------------------------------------------------------- mod
Step "Installing the mod..."
try {
    $modsDir = Join-Path $Game 'Mods'
    New-Item -ItemType Directory -Path (Join-Path $modsDir 'LiveEditor') -Force | Out-Null
    Copy-Item (Join-Path $Root 'mod\LiveEditor.dll') -Destination $modsDir -Force
    $mf = Join-Path $Root 'mod\manifest.json'
    if (Test-Path $mf) { Copy-Item $mf -Destination (Join-Path $modsDir 'LiveEditor') -Force }
    Good "Mods\LiveEditor.dll"
} catch {
    Fail "Could not install the mod: $($_.Exception.Message)"
}

# ----------------------------------------------------------------------- app
Step "Installing the control app..."
$appDir = Join-Path $Game 'LiveEditorApp'
try {
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null
    Copy-Item (Join-Path $Root 'app\*') -Destination $appDir -Recurse -Force
    Good $appDir
} catch {
    Fail "Could not install the app: $($_.Exception.Message)"
}

$exe = Join-Path $appDir 'LiveEditorApp.exe'
if (Test-Path $exe) {
    try {
        $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SOTF Live Editor.lnk'
        $sc = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk)
        $sc.TargetPath = $exe
        $sc.WorkingDirectory = $appDir
        $sc.Description = 'Sons Of The Forest - Live Editor'
        $sc.Save()
        Good "Desktop shortcut: SOTF Live Editor"
    } catch {
        Warn "Could not create the Desktop shortcut (the app still works from $appDir)"
    }
}

# --------------------------------------------------------------------- done
Say ""
if ($Failed) {
    Say "  Finished with errors - see the red lines above."
    Start-Sleep -Seconds 25
} else {
    Write-Host "  Done." -ForegroundColor Green
    Say ""
    Say "  1. Start Sons Of The Forest and load a save."
    Say "  2. Open 'SOTF Live Editor' from your Desktop."
    Say ""
    Say "  Multiplayer: you must be the host, or have cheats enabled."
    Say ""
    Start-Sleep -Seconds 10
}
