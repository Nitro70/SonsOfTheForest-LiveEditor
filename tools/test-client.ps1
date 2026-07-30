# Minimal NDJSON test client for LiveEditor (PLAN.md section 4). Reads the port/token
# from UserData\LiveEditor.port, completes the hello handshake, then sends whatever
# commands are passed in -Commands (JSON args optional per command via -Args).
#
# Usage:
#   .\test-client.ps1                                   # hello + ping + state.get + describe
#   .\test-client.ps1 -Commands "ping","state.get"
param(
    [string[]]$Commands = @("ping", "state.get", "describe"),
    [string]$GameDir = (& "$PSScriptRoot\Find-GameDir.ps1"),
    [string]$PortFilePath,
    [int]$TimeoutMs = 5000
)

if (-not $PortFilePath) {
    # LoaderEnvironment.UserDataDirectory should be <gameroot>\UserData; verify by
    # falling back to a search if the direct path doesn't have the file yet.
    $candidate = "$GameDir\UserData\LiveEditor.port"
    $PortFilePath = if (Test-Path $candidate) { $candidate } else {
        Get-ChildItem $GameDir -Recurse -Filter "LiveEditor.port" -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

if (-not $PortFilePath -or -not (Test-Path $PortFilePath)) {
    Write-Error "LiveEditor.port not found (looked at $GameDir\UserData\LiveEditor.port and searched $GameDir). Is the mod loaded and OnSdkInitialized fired?"
    exit 1
}

$lines = Get-Content $PortFilePath
$port = [int]$lines[0]
$token = $lines[1]
Write-Host "Connecting to 127.0.0.1:$port (token file: $PortFilePath)"

$client = New-Object System.Net.Sockets.TcpClient
$client.Connect("127.0.0.1", $port)
$stream = $client.GetStream()
# BOM-less UTF8: the [System.Text.Encoding]::UTF8 singleton writes a 3-byte BOM on
# the stream's first flush, which the server's parser then chokes on if it lands
# inside the first JSON line. Always construct UTF8Encoding($false) for NDJSON.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$writer = New-Object System.IO.StreamWriter($stream, $utf8NoBom)
$writer.NewLine = "`n"
$writer.AutoFlush = $true
$reader = New-Object System.IO.StreamReader($stream, $utf8NoBom)

function Send-Line([string]$json) {
    Write-Host "-> $json"
    $writer.WriteLine($json)
}

function Read-LineWithTimeout([int]$ms) {
    $task = $reader.ReadLineAsync()
    if ($task.Wait($ms)) { return $task.Result }
    Write-Error "timed out waiting for a response"
    return $null
}

$id = 1
Send-Line (@{ id = $id; cmd = "hello"; args = @{ protocol = 1; token = $token; client = "test-client.ps1/0.1" } } | ConvertTo-Json -Compress)
$resp = Read-LineWithTimeout $TimeoutMs
Write-Host "<- $resp"
$respObj = $resp | ConvertFrom-Json
if (-not $respObj.ok) { Write-Error "hello failed, aborting"; $client.Close(); exit 1 }

foreach ($cmd in $Commands) {
    $id++
    Send-Line (@{ id = $id; cmd = $cmd } | ConvertTo-Json -Compress)
    $resp = Read-LineWithTimeout $TimeoutMs
    Write-Host "<- $resp"
}

$client.Close()
Write-Host "done"
