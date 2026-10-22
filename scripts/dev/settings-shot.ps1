# Starts a development build with the Settings window open at a section and
# saves a picture of the window's content (drawn by the app itself, never a
# grab of the screen), then stops it again. Like board-shot.ps1, it runs on a
# copy of the development data (%LOCALAPPDATA%\Tack.Dev) under a
# single-instance name of its own, so several can run at once and none touches
# the others' files; the copy is deleted afterwards. Nothing leaves the machine.
#
# -Section is where Settings opens: "top", "phone" (the phone page) or a group
# of the list ("general", "speed", "appearance", "shortcuts", "updates").
# -Theme draws it "light" or "dark" whatever Windows uses.
param(
    [string]$Section = "top",
    [ValidateSet("", "light", "dark")]
    [string]$Theme = "",
    # The newest debug build: a solution build lands under bin\x64, a project build under bin.
    [string]$Exe = (Get-ChildItem "$PSScriptRoot\..\..\src\Tack.App\bin" -Recurse -Filter Tack.exe |
        Where-Object { $_.FullName -match '\\Debug\\' -and $_.FullName -notmatch '\\native\\' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName,
    [string]$Out = "$env:TEMP\tack-settings.png",
    [int]$TimeoutMs = 15000
)
$Out = [IO.Path]::GetFullPath($Out)
# A build of the whole solution puts the app under its platform's folder.
$platformBuild = $Exe -replace '\\bin\\Debug\\', '\bin\x64\Debug\'
if (-not (Test-Path -LiteralPath $Exe) -and (Test-Path -LiteralPath $platformBuild)) { $Exe = $platformBuild }
Remove-Item -LiteralPath $Out -ErrorAction SilentlyContinue
$env:TACK_DEBUG_SETTINGS = $Section
$env:TACK_DEBUG_SETTINGS_SHOT = $Out
$env:TACK_DEBUG_SETTINGS_THEME = $Theme
$env:TACK_STAY_IN_PACKAGE = "1"
# The copy is thrown away: its sweep must not send anything to the Recycle Bin.
$env:TACK_DEBUG_NO_SWEEP = "1"
$scratch = Join-Path ([IO.Path]::GetTempPath()) "tack-settings-$([guid]::NewGuid().ToString('N'))"
$dev = Join-Path $env:LOCALAPPDATA "Tack.Dev"
if (Test-Path -LiteralPath $dev) { Copy-Item -LiteralPath $dev -Destination $scratch -Recurse } else { New-Item -ItemType Directory $scratch | Out-Null }
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
Get-Content $log | Select-String "settings picture|unhandled|failed"
Remove-Item $log -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $Out) { "settings picture: $Out" } else { "no settings picture"; exit 1 }
