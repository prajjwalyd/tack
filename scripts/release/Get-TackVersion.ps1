<#
.SYNOPSIS
Prints Tack's version, the <Version> in Directory.Build.props. The release
tag, the installer's name and the version in latest.json all follow it.

.EXAMPLE
./scripts/release/Get-TackVersion.ps1
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$props = [xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))
$version = @($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })
if ($version.Count -ne 1) {
    throw 'Directory.Build.props should hold exactly one <Version>.'
}
$version[0].Trim()
