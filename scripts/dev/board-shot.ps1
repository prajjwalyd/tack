# Starts a development build with the board down and saves a picture of the
# board alone (the app grabs only its own rectangle), then stops it again.
# It runs on a copy of the development data (%LOCALAPPDATA%\Tack.Dev) under a
# single-instance name of its own, so several can run at once and none touches
# the others' files; the copy is deleted afterwards. Nothing leaves the machine.
#
# -Felt, -Frame and -Paper picture another look (a felt preset's id, "accent"
# or "#rrggbb"; a finish's id; a paper's id): they are written into the copy's
# board.json only.
param(
    # The newest debug build: a solution build lands under bin\x64, a project build under bin.
    [string]$Exe = (Get-ChildItem "$PSScriptRoot\..\..\src\Tack.App\bin" -Recurse -Filter Tack.exe |
        Where-Object { $_.FullName -match '\\Debug\\' -and $_.FullName -notmatch '\\native\\' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName,
    [string]$Out = "$env:TEMP\tack-board.png",
    [int]$TimeoutMs = 15000,
    [string]$Felt,
    [string]$Frame,
    [string]$Paper
)
$Out = [IO.Path]::GetFullPath($Out)
# A build of the whole solution puts the app under its platform's folder.
$platformBuild = $Exe -replace '\\bin\\Debug\\', '\bin\x64\Debug\'
if (-not (Test-Path -LiteralPath $Exe) -and (Test-Path -LiteralPath $platformBuild)) { $Exe = $platformBuild }
Remove-Item -LiteralPath $Out -ErrorAction SilentlyContinue
$env:TACK_DEBUG_SHOW = "1"
$env:TACK_DEBUG_SHOT = $Out
$env:TACK_STAY_IN_PACKAGE = "1"
# The copy is thrown away: its sweep must not send anything to the Recycle Bin.
$env:TACK_DEBUG_NO_SWEEP = "1"
$scratch = Join-Path ([IO.Path]::GetTempPath()) "tack-shot-$([guid]::NewGuid().ToString('N'))"
$dev = Join-Path $env:LOCALAPPDATA "Tack.Dev"
if (Test-Path -LiteralPath $dev) { Copy-Item -LiteralPath $dev -Destination $scratch -Recurse } else { New-Item -ItemType Directory $scratch | Out-Null }
$look = @{ felt = $Felt; frame = $Frame; paper = $Paper }.GetEnumerator() | Where-Object { $_.Value }
if ($look) {
    $file = Join-Path $scratch "Roaming\board.json"
    if (-not (Test-Path -LiteralPath $file)) { throw "the development data has no board.json to change the look in" }
    $board = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if (-not $board.appearance) { $board | Add-Member appearance ([pscustomobject]@{}) }
    foreach ($entry in $look) { $board.appearance | Add-Member $entry.Key $entry.Value -Force }
    [IO.File]::WriteAllText($file, ($board | ConvertTo-Json -Depth 20))
}
$env:TACK_HOME = $scratch
$env:TACK_DEV_INSTANCE = Split-Path $scratch -Leaf
$log = [IO.Path]::GetTempFileName()
$process = Start-Process (Resolve-Path $Exe).Path -PassThru -RedirectStandardError $log
try {
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while (-not (Test-Path -LiteralPath $Out) -and (Get-Date) -lt $deadline -and -not $process.HasExited) {
        Start-Sleep -Milliseconds 200
    }
    Start-Sleep -Milliseconds 300
}
finally {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
Get-Content $log | Select-String "board picture|reveal|tuck|failed"
Remove-Item $log -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $Out) { "board picture: $Out" } else { "no board picture"; exit 1 }
