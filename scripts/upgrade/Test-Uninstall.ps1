<#
.SYNOPSIS
Uninstalls Tack silently, as a script or IT would, and checks that Tack is
gone while the person's data stays: a silent uninstall never deletes it.

.DESCRIPTION
Tack is left running first, so the uninstaller has to end it.

Part of the upgrade check (see Common.ps1): only on a throwaway machine.

.EXAMPLE
./scripts/upgrade/Test-Uninstall.ps1 -Snapshot $env:RUNNER_TEMP/before.json
#>
param([Parameter(Mandatory)] [string] $Snapshot)
. "$PSScriptRoot/Common.ps1"

$before = Read-Snapshot $Snapshot
$uninstaller = Join-Path $TackLocal 'unins000.exe'
Assert ($null -ne (Get-TackHost)) 'Tack is running before the uninstall'

# The first copy waits for the one it runs from %TEMP% to finish.
$process = Start-Process $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$null = $process.Handle
if (-not $process.WaitForExit(120000)) { throw 'The uninstaller did not finish within 2 minutes.' }
Write-Host "The uninstaller exited with $($process.ExitCode)."
Assert ($process.ExitCode -eq 0) 'the uninstaller succeeded'
# It deletes itself a moment after.
[void](Wait-Until { -not (Test-Path -LiteralPath $uninstaller) } 30)

Write-Host 'Tack is gone:'
Assert (@(Get-TackProcesses).Count -eq 0) 'no Tack is running'
Assert (-not (Test-Path -LiteralPath (Join-Path $TackLocal 'Tack.exe'))) 'Tack.exe is gone'
Assert (-not (Test-Path -LiteralPath $uninstaller)) 'the uninstaller is gone'
Assert (-not (Test-Path -LiteralPath $NewUninstallKey)) 'the Apps entry is gone'
Assert ($null -eq (Get-RunValue)) 'the Run value is gone'
Assert (-not (Test-Path -LiteralPath $Shortcut)) 'the Start menu shortcut is gone'
$left = @(Get-ChildItem -LiteralPath $TackLocal -Force | ForEach-Object Name)
Assert (-not ($left | Where-Object { $_ -notin 'Captures', 'Notes' })) "only Captures and Notes are left in $TackLocal ($($left -join ', '))"

Write-Host "The person's data stays:"
Assert-SameData $before
Complete-Checks 'A silent uninstall'
