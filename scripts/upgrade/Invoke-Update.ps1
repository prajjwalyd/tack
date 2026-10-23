<#
.SYNOPSIS
Runs Tack's installer the way an updater does, "/P /UPDATE /R /ARGS", and
waits for it and for the Tack it starts.

.DESCRIPTION
With -AsTack02, the running Tack 0.2 is ended right after the installer
starts, as 0.2's updater (tauri-plugin-updater) quits once it has started
the installer. Without it, the running Tack is left for the installer to
end after its 10 seconds' wait.

Part of the upgrade check (see Common.ps1): only on a throwaway machine.

.EXAMPLE
./scripts/upgrade/Invoke-Update.ps1 -Installer artifacts/installer/Tack_0.3.0_x64-setup.exe -AsTack02
#>
param(
    [Parameter(Mandatory)] [string] $Installer,
    [switch] $AsTack02
)
. "$PSScriptRoot/Common.ps1"

$Installer = (Resolve-Path -LiteralPath $Installer).Path
$before = @(Get-TackProcesses)
Write-Host "Running: $($before | ForEach-Object { "$($_.Path) ($($_.Id))" })"
$started = Get-Date

$setup = Start-Process $Installer -ArgumentList '/P', '/UPDATE', '/R', '/ARGS' -PassThru
# Windows PowerShell reads ExitCode only from a process whose handle it opened early.
$null = $setup.Handle
if ($AsTack02) {
    Start-Sleep -Milliseconds 300
    $before | ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    Write-Host 'Ended Tack 0.2, as its updater quits after starting the installer.'
}
if (-not $setup.WaitForExit(180000)) {
    Show-SetupLog
    throw 'Setup did not finish within 3 minutes: is it waiting for a click?'
}
Write-Host "Setup exited with $($setup.ExitCode) after $([int]((Get-Date) - $started).TotalSeconds) s."
Show-SetupLog
Assert ($setup.ExitCode -eq 0) "Setup succeeded (exit code $($setup.ExitCode))"

# /R: Setup starts the new Tack once it is done.
$appExe = Join-Path $TackLocal 'Tack.exe'
$up = Wait-Until { $tack = Get-TackHost; $tack -and (Test-SamePath $tack.Path $appExe) } 60
$tack = Get-TackHost
Assert $up "Setup started the new Tack from $appExe (host window owned by $($tack.Path))"
Assert (-not ($before | Where-Object { $tack -and $_.Id -eq $tack.Id })) 'the Tack running now is a new process'
Assert (@(Get-TackProcesses).Count -eq 1) "exactly one Tack runs ($(@(Get-TackProcesses | ForEach-Object Path) -join ', '))"
Complete-Checks 'The update'
