<#
.SYNOPSIS
Installs Inno Setup 6.7.3 into a folder of its own, for CI, and prints the
path of its ISCC.exe.

.DESCRIPTION
The installer comes from Inno Setup's GitHub release and is checked against
a pinned SHA-256 (GitHub's digest of that release asset) before it runs.
Every build uses this exact version, whatever the runner image has. Under
GitHub Actions, ISCC is also set in the job's environment, which
Build-Installer.ps1 reads. On your own PC, install Inno Setup 6.7 yourself
(https://jrsoftware.org/isdl.php) instead.

.EXAMPLE
./scripts/release/Install-InnoSetup.ps1 -Dir $env:RUNNER_TEMP/inno-setup
#>
param([string] $Dir = (Join-Path ([IO.Path]::GetTempPath()) 'inno-setup-6.7.3'))
$ErrorActionPreference = 'Stop'

$version = '6.7.3'
$url = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
$sha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'

$Dir = [IO.Path]::GetFullPath($Dir)
$iscc = Join-Path $Dir 'ISCC.exe'
if (-not (Test-Path $iscc)) {
    $setup = Join-Path ([IO.Path]::GetTempPath()) "innosetup-$version.exe"
    Write-Host "Downloading Inno Setup $version"
    Invoke-WebRequest $url -OutFile $setup -UseBasicParsing
    $actual = (Get-FileHash $setup -Algorithm SHA256).Hash
    if ($actual -ne $sha256) {
        Remove-Item $setup
        throw "innosetup-$version.exe has SHA-256 $actual, expected $sha256."
    }

    $arguments = '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/CURRENTUSER', "/DIR=`"$Dir`""
    $process = Start-Process $setup -ArgumentList $arguments -Wait -PassThru
    Remove-Item $setup
    if ($process.ExitCode -ne 0 -or -not (Test-Path $iscc)) {
        throw "Inno Setup's installer failed (exit code $($process.ExitCode))."
    }
}

if ($env:GITHUB_ENV) {
    Add-Content $env:GITHUB_ENV "ISCC=$iscc"
}
$iscc
