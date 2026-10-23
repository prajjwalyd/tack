<#
.SYNOPSIS
Checks that this Tack is installed where 0.2 was, with nothing of 0.2 left
behind and every bit of the person's data as it was in the snapshot.

.DESCRIPTION
Part of the upgrade check (see Common.ps1): only on a throwaway machine.

.EXAMPLE
./scripts/upgrade/Test-Installed.ps1 -Snapshot $env:RUNNER_TEMP/before.json -Version 0.3.0
#>
param(
    [Parameter(Mandatory)] [string] $Snapshot,
    [Parameter(Mandatory)] [string] $Version
)
. "$PSScriptRoot/Common.ps1"

$before = Read-Snapshot $Snapshot
$appExe = Join-Path $TackLocal 'Tack.exe'

# Give the new Tack time to put the board back and save it once.
Start-Sleep -Seconds 15

Write-Host 'This Tack:'
Assert (Test-Path -LiteralPath $appExe) "Tack.exe is in $TackLocal"
$fileVersion = (Get-Item -LiteralPath $appExe).VersionInfo.FileVersion
Assert ($fileVersion -like "$($Version.Split('-')[0])*") "Tack.exe is version $Version (file version $fileVersion)"
Assert (Test-Path -LiteralPath (Join-Path $TackLocal 'unins000.exe')) 'its uninstaller is there'
Assert (Test-Path -LiteralPath $NewUninstallKey) 'it has its Apps entry'
$entry = Get-ItemProperty -LiteralPath $NewUninstallKey -ErrorAction SilentlyContinue
Assert ($entry.DisplayVersion -eq $Version) "the Apps entry says $Version ($($entry.DisplayVersion))"
Assert ($entry.DisplayName -eq 'Tack') "the Apps entry is named Tack ($($entry.DisplayName))"
Assert ($entry.UninstallString -match '/SILENT /ASK$') "uninstalling from Apps asks Tack's question only ($($entry.UninstallString))"
$tack = Get-TackHost
Assert ($tack -and (Test-SamePath $tack.Path $appExe)) "Tack is running from $appExe"

Write-Host 'Nothing of 0.2 left:'
Assert (-not (Test-Path -LiteralPath $OldUninstallKey)) "0.2's Apps entry is gone"
Assert (-not (Test-Path -LiteralPath 'HKCU:\Software\tack\Tack')) "0.2's key HKCU\Software\tack\Tack is gone"
Assert (-not (Test-Path -LiteralPath (Join-Path $TackLocal 'uninstall.exe'))) "0.2's uninstall.exe is gone"
Assert (-not (Test-Path -LiteralPath (Join-Path $TackLocal 'WebView2'))) "0.2's WebView2 folder is gone"
Assert (-not (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'app.tack.board'))) "0.2's app.tack.board folder is gone"
$apps = @(Get-ChildItem -LiteralPath $UninstallKey | Get-ItemProperty -ErrorAction SilentlyContinue | Where-Object DisplayName -eq 'Tack')
Assert ($apps.Count -eq 1) "Apps lists Tack once ($($apps.Count))"

Write-Host 'Start with Windows and the Start menu:'
$run = Get-RunValue
Assert ($run -and (Test-SamePath $run.Trim('"') $appExe)) "the Run value starts $appExe ($run)"
if ($before.Facts.HadShortcut) {
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut).TargetPath
    Assert (Test-SamePath $target $appExe) "the Start menu shortcut starts $appExe ($target)"
}
else {
    Assert (-not (Test-Path -LiteralPath $Shortcut)) 'an update adds no Start menu shortcut the person had deleted'
}

Write-Host "The person's data:"
Assert-SameData $before
Write-Host 'board.json now:'
Get-Content -LiteralPath $BoardFile | Write-Host
Complete-Checks "Tack $Version installed"
