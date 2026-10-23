<#
.SYNOPSIS
Signs an installer for the updater with the Tauri CLI, writing <installer>.sig,
and checks that the signature names the version and the file.

.DESCRIPTION
The key comes from TAURI_SIGNING_PRIVATE_KEY (its contents) or
TAURI_SIGNING_PRIVATE_KEY_PATH, and its password from
TAURI_SIGNING_PRIVATE_KEY_PASSWORD, as for `tauri signer sign`. The CLI binds
the signature to -Version (`--app-version`), which every installed Tack, 0.2
included, requires.

The CLI is pinned to the version 0.2's releases were signed with. Run once
with -Fetch first: that downloads it into npm's cache while no key is in the
environment, and signing then runs npm offline, so nothing is downloaded
while the key is.

Never sign with the real key outside the release workflow. To rehearse, make
a throwaway key: npx @tauri-apps/cli@2.12.1 signer generate -w <file>

.EXAMPLE
./scripts/release/Sign-Installer.ps1 -Fetch
$env:TAURI_SIGNING_PRIVATE_KEY_PATH = 'C:\scratch\test.key'
$env:TAURI_SIGNING_PRIVATE_KEY_PASSWORD = 'throwaway'
./scripts/release/Sign-Installer.ps1 -Installer artifacts/installer/Tack_0.3.0_x64-setup.exe -Version 0.3.0
#>
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Fetch')] [switch] $Fetch,
    [Parameter(Mandatory, ParameterSetName = 'Sign')] [string] $Installer,
    [Parameter(Mandatory, ParameterSetName = 'Sign')] [string] $Version
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TauriSignature.ps1')

# 2.12.1 signed 0.2's releases and has `signer sign --app-version`.
$tauriCli = '@tauri-apps/cli@2.12.1'

# npx.cmd, not the npx.ps1 PowerShell finds first: that one re-reads the
# calling line's source text to rebuild its arguments, and from a script it
# gets them wrong.
$npx = if ([Environment]::OSVersion.Platform -eq 'Win32NT') { 'npx.cmd' } else { 'npx' }

if ($Fetch) {
    & $npx --yes $tauriCli --version | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not fetch $tauriCli."
    }
    return
}

if (-not $env:TAURI_SIGNING_PRIVATE_KEY -and -not $env:TAURI_SIGNING_PRIVATE_KEY_PATH) {
    throw 'Set TAURI_SIGNING_PRIVATE_KEY (the key itself) or TAURI_SIGNING_PRIVATE_KEY_PATH.'
}
# Without it the CLI would wait for the password to be typed.
if (-not $env:TAURI_SIGNING_PRIVATE_KEY_PASSWORD) {
    throw "Set TAURI_SIGNING_PRIVATE_KEY_PASSWORD to the key's password."
}

$Installer = (Resolve-Path $Installer).Path
$sig = "$Installer.sig"
if (Test-Path $sig) {
    Remove-Item $sig
}

$offline = $env:npm_config_offline
$env:npm_config_offline = 'true'
try {
    & $npx --yes $tauriCli signer sign --app-version $Version $Installer | Write-Host
    $exitCode = $LASTEXITCODE
} finally {
    $env:npm_config_offline = $offline
}
if ($exitCode -ne 0) {
    throw "Signing failed (exit code $exitCode). If npm could not find the CLI offline, run with -Fetch first."
}
if (-not (Test-Path $sig)) {
    throw "The CLI wrote no $sig."
}

$signature = Read-TauriSignature (Get-Content -Raw $sig)
$name = Split-Path $Installer -Leaf
if ($signature.Version -ne $Version -or $signature.File -ne $name) {
    throw "The signature is for '$($signature.File)' version '$($signature.Version)', not '$name' version '$Version'."
}
Write-Host "Signed $name for version $Version with key $($signature.KeyId)."
$sig
