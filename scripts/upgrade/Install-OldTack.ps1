<#
.SYNOPSIS
Installs a released Tack 0.2 for the current user, as a person would have:
its NSIS installer from the GitHub release, checked against a pinned
SHA-256, run silently.

.DESCRIPTION
Part of the upgrade check (see Common.ps1): only on a throwaway machine.

.EXAMPLE
./scripts/upgrade/Install-OldTack.ps1 -Version 0.2.0
#>
param([Parameter(Mandatory)] [ValidateSet('0.2.0', '0.2.1')] [string] $Version)
. "$PSScriptRoot/Common.ps1"

# GitHub's digests of the x64 installers on the v0.2.0 and v0.2.1 releases.
$sha256 = @{
    '0.2.0' = 'fd83a6bdcc9151d8c12615b1b3aeeea7183b9a1a175bf61fa4958724370fc315'
    '0.2.1' = 'd751f4959ac52a0a92795369d286de2b8bfe82dfe2258de620ec8a08642d438e'
}[$Version]
$name = "Tack_${Version}_x64-setup.exe"
$url = "https://github.com/prajjwalyd/tack/releases/download/v$Version/$name"

if (Test-Path -LiteralPath $OldUninstallKey) { throw "A Tack 0.2 is already installed here." }
if (Test-Path -LiteralPath $NewUninstallKey) { throw "A Tack is already installed here." }

$folder = Join-Path ([IO.Path]::GetTempPath()) "tack-$Version-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $folder | Out-Null
$setup = Join-Path $folder $name
Write-Host "Downloading $url"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest $url -OutFile $setup -UseBasicParsing
$actual = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
if ($actual -ne $sha256) { throw "$name has SHA-256 $actual, expected $sha256." }

# NSIS's /S: silent, per user, into %LOCALAPPDATA%\Tack.
$process = Start-Process $setup -ArgumentList '/S' -Wait -PassThru
Remove-Item -LiteralPath $folder -Recurse -Force
if ($process.ExitCode -ne 0) { throw "Tack $Version's installer exited with $($process.ExitCode)." }

Write-Host "Tack $Version installed:"
Get-ItemProperty -LiteralPath $OldUninstallKey | Select-Object DisplayName, DisplayVersion, InstallLocation, UninstallString | Format-List | Out-String | Write-Host
Assert (Test-Path -LiteralPath $OldUninstallKey) "Tack $Version has its Apps entry"
Assert ((Get-ItemProperty -LiteralPath $OldUninstallKey).DisplayVersion -eq $Version) "the Apps entry says $Version"
Assert (Test-Path -LiteralPath (Join-Path $TackLocal 'tack.exe')) "tack.exe is in $TackLocal"
Assert (Test-Path -LiteralPath (Join-Path $TackLocal 'uninstall.exe')) "uninstall.exe is in $TackLocal"
Write-Host "  Start menu shortcut: $(if (Test-Path -LiteralPath $Shortcut) { $Shortcut } else { 'none' })"
Complete-Checks "Installing Tack $Version"
