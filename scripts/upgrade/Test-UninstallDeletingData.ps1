<#
.SYNOPSIS
Installs Tack afresh, then uninstalls it as a person would from Apps, with
"Delete the application data" ticked, and checks that it asked that one
question only and that the board, captures and notes are gone with it.

.DESCRIPTION
The uninstaller's windows are answered through UI Automation
(UninstallDialogs.ps1). Every question it asks is listed in the log and in
the job summary.

Part of the upgrade check (see Common.ps1): only on a throwaway machine.

.EXAMPLE
./scripts/upgrade/Test-UninstallDeletingData.ps1 -Installer artifacts/installer/Tack_0.3.0_x64-setup.exe
#>
param([Parameter(Mandatory)] [string] $Installer)
. "$PSScriptRoot/Common.ps1"
. "$PSScriptRoot/UninstallDialogs.ps1"

$Installer = (Resolve-Path -LiteralPath $Installer).Path
$setup = Start-Process $Installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru
$null = $setup.Handle
if (-not $setup.WaitForExit(180000)) { throw 'Setup did not finish within 3 minutes.' }
Assert ($setup.ExitCode -eq 0) "a fresh install succeeded (exit code $($setup.ExitCode))"
Assert (Test-Path -LiteralPath $BoardFile) 'the board from before is still there to delete'

# What Apps runs: its UninstallString, "<unins000.exe>" and its arguments.
$command = (Get-ItemProperty -LiteralPath $NewUninstallKey).UninstallString
if ($command -notmatch '^"([^"]+)"\s*(.*)$') { throw "Cannot read the UninstallString: $command" }
$uninstaller = $Matches[1]
Write-Host "Apps runs: $command"
$process = Start-Process $uninstaller -ArgumentList $Matches[2] -PassThru
$answers = Invoke-UninstallerDialogs $process
[void](Wait-Until { -not (Test-Path -LiteralPath $uninstaller) } 30)

$questions = @($answers | Where-Object { $_ -notmatch '-> OK$' })
if ($env:GITHUB_STEP_SUMMARY) {
    $lines = @('### Uninstalling with "Delete the application data"', '') + @($answers | ForEach-Object { "- $_" }) + ''
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, ($lines -join "`n"))
}
Assert ($answers | Where-Object { $_ -match 'Delete the application data: ticked' }) 'the uninstaller offered "Delete the application data" and it was ticked'
Assert ($questions.Count -eq 1) "the uninstaller asked one question ($($questions.Count): $($questions -join ' | '))"

Write-Host 'Tack and its data are gone:'
Assert (@(Get-TackProcesses).Count -eq 0) 'no Tack is running'
Assert (-not (Test-Path -LiteralPath $NewUninstallKey)) 'the Apps entry is gone'
Assert (-not (Test-Path -LiteralPath $TackRoaming)) "$TackRoaming (board.json) is gone"
Assert (-not (Test-Path -LiteralPath $TackLocal)) "$TackLocal (captures and notes) is gone"
Assert (Test-Path -LiteralPath $OwnPicture) "the person's own picture, which Tack only showed, is still there"
Complete-Checks 'An uninstall that deletes the data'
