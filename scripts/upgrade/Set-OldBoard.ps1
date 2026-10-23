<#
.SYNOPSIS
Gives the installed Tack 0.2 a board as a person's would be, starts it, and
writes a snapshot of that data for the later checks.

.DESCRIPTION
The board is OldBoard.ps1's, with "Start with Windows" on (the Run value as
0.2 writes it). Tack 0.2 is then left running, as it is when its updater
starts the new installer.

Part of the upgrade check (see Common.ps1): only on a throwaway machine.

.EXAMPLE
./scripts/upgrade/Set-OldBoard.ps1 -Snapshot $env:RUNNER_TEMP/before.json
#>
param([Parameter(Mandatory)] [string] $Snapshot)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/OldBoard.ps1"
Write-OldBoard

$oldExe = Join-Path $TackLocal 'tack.exe'
New-Item -Path $RunKey -Force | Out-Null
Set-ItemProperty -Path $RunKey -Name 'Tack' -Value "`"$oldExe`""

Write-Host "Starting $oldExe"
Start-Process $oldExe -WorkingDirectory $TackLocal | Out-Null
# Time to read the board, open its web view and settle.
Start-Sleep -Seconds 20
$running = @(Get-TackProcesses | Where-Object { Test-SamePath $_.Path $oldExe })
Write-Host "  Tack 0.2 running: $($running.Count) process(es)"
Write-Host "  WebView2 folders: $(@((Join-Path $TackLocal 'WebView2'), (Join-Path $env:LOCALAPPDATA 'app.tack.board') | Where-Object { Test-Path -LiteralPath $_ }) -join ', ')"
Write-Host "board.json as Tack 0.2 left it:"
Get-Content -LiteralPath $BoardFile | Write-Host

$before = Save-Snapshot $Snapshot @{
    OldRunning  = $running.Count -gt 0
    HadShortcut = Test-Path -LiteralPath $Shortcut
}
Assert $before.Facts.OldRunning "Tack 0.2 started and is running"
Assert (@($before.Prints).Count -eq 3) "Tack 0.2 kept the three prints it was given"
Assert ((Get-RunValue) -eq "`"$oldExe`"") "the Run value is 0.2's"
Complete-Checks 'Tack 0.2 with a board'
