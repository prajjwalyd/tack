<#
.SYNOPSIS
Compiles Tack's installer (installer/Tack.iss) from a Native AOT publish
folder and prints its path. A compiler warning fails the build.

.DESCRIPTION
Writes Tack_<version>_<x64|arm64>-setup.exe. The version defaults to the one
in Directory.Build.props. ISCC.exe is taken from -Iscc, the ISCC environment
variable (Install-InnoSetup.ps1 sets it in CI), PATH, or the usual Inno
Setup 6 folders.

Never run the installer this makes on a PC whose installed Tack you want to
keep: it replaces it. Try it on a virtual machine.

.EXAMPLE
dotnet publish src/Tack.App -c Release -r win-x64 -o artifacts/win-x64
./scripts/release/Build-Installer.ps1 -PublishDir artifacts/win-x64 -Arch x64
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [Parameter(Mandatory)] [ValidateSet('x64', 'arm64')] [string] $Arch,
    [string] $Version,
    [string] $OutputDir,
    [string] $Iscc
)
$ErrorActionPreference = 'Stop'

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Find-Iscc {
    $candidates = @(
        $env:ISCC
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $found = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $found) {
        throw 'ISCC.exe not found: install Inno Setup 6.7, or pass -Iscc.'
    }
    $found
}

if (-not $Version) {
    $Version = & (Join-Path $PSScriptRoot 'Get-TackVersion.ps1')
}
if (-not $Iscc) {
    $Iscc = Find-Iscc
}
if (-not $OutputDir) {
    $OutputDir = Join-Path $root 'artifacts\installer'
}
$PublishDir = (Resolve-Path $PublishDir).Path
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

$isccArgs = @(
    "/DAppVersion=$Version"
    "/DAppArch=$Arch"
    "/DPublishDir=$PublishDir"
    "/O$OutputDir"
    (Join-Path $root 'installer\Tack.iss')
)
Write-Host "$Iscc $($isccArgs -join ' ')"

# ISCC writes errors to stderr; read both streams as text, without letting
# Windows PowerShell turn stderr lines into errors.
$output = & {
    $ErrorActionPreference = 'Continue'
    & $Iscc @isccArgs 2>&1 | ForEach-Object { "$_" }
}
$exitCode = $LASTEXITCODE
$output | Where-Object { $_ -notmatch '^\s*(Compressing|Parsing|Reading|Updating)\b' } | ForEach-Object { Write-Host $_ }

if ($exitCode -ne 0) {
    throw "ISCC failed with exit code $exitCode."
}
$warnings = @($output | Where-Object { $_ -match '^\s*Warning\b' })
if ($warnings.Count -gt 0) {
    throw "ISCC warned $($warnings.Count) time(s); the installer must compile cleanly."
}

$installer = Join-Path $OutputDir "Tack_${Version}_$Arch-setup.exe"
if (-not (Test-Path $installer)) {
    throw "ISCC succeeded but $installer is not there."
}
$installer
