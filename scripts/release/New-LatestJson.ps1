<#
.SYNOPSIS
Writes latest.json, the update feed, from the two installers' signatures.

.DESCRIPTION
The shape is 0.2's exactly (what tauri-action wrote), since installed copies
of 0.2 read it as well as the C# Tack:

    { "version", "notes", "pub_date",
      "platforms": { "windows-x86_64", "windows-x86_64-nsis",
                     "windows-aarch64", "windows-aarch64-nsis" } }

each platform with the .sig file's text as "signature" and the installer's
download URL in the release for -Tag as "url". The "-nsis" entries repeat
the plain ones, as 0.2's did.

.EXAMPLE
./scripts/release/New-LatestJson.ps1 -Version 0.3.0 -Tag v0.3.0 -Repository prajjwalyd/tack `
    -X64Signature Tack_0.3.0_x64-setup.exe.sig -Arm64Signature Tack_0.3.0_arm64-setup.exe.sig -OutFile latest.json
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Tag,
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $X64Signature,
    [Parameter(Mandatory)] [string] $Arm64Signature,
    [Parameter(Mandatory)] [string] $OutFile,
    [string] $Notes = ''
)
$ErrorActionPreference = 'Stop'

# A JSON string literal, escaped the same in Windows PowerShell and pwsh.
function Quote([string] $Text) {
    ConvertTo-Json -InputObject $Text -Compress
}

function Entry([string] $Arch, [string] $SignatureFile) {
    $installer = "Tack_${Version}_$Arch-setup.exe"
    if ((Split-Path $SignatureFile -Leaf) -ne "$installer.sig") {
        throw "$SignatureFile should be named $installer.sig."
    }
    $signature = (Get-Content -Raw $SignatureFile).Trim()
    if (-not $signature) {
        throw "$SignatureFile is empty."
    }
    $url = "https://github.com/$Repository/releases/download/$Tag/$installer"
    "{`n      `"signature`": $(Quote $signature),`n      `"url`": $(Quote $url)`n    }"
}

$x64 = Entry 'x64' $X64Signature
$arm64 = Entry 'arm64' $Arm64Signature
$pubDate = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)

# Written by hand so the key order and layout are 0.2's (JSON.stringify with
# two-space indents), whichever PowerShell runs this.
$json = @"
{
  "version": $(Quote $Version),
  "notes": $(Quote $Notes),
  "pub_date": $(Quote $pubDate),
  "platforms": {
    "windows-x86_64": $x64,
    "windows-x86_64-nsis": $x64,
    "windows-aarch64": $arm64,
    "windows-aarch64-nsis": $arm64
  }
}
"@ -replace "`r`n", "`n"

$null = $json | ConvertFrom-Json
$path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
[IO.File]::WriteAllText($path, $json, (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $OutFile for version $Version."
