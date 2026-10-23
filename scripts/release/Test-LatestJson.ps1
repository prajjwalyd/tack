<#
.SYNOPSIS
Checks latest.json before a release is published, as 0.2's verify job did,
and a little more. A wrong update file would hand every installed Tack a
failing update, so any problem fails.

.DESCRIPTION
Checks that the file has 0.2's shape; that its version is -Version; that
both builds are there, each "-nsis" entry the same as its plain one, with
the URL of the right installer in this repository's release for -Tag; and
that each signature's trusted comment names that installer and -Version
and was made with the updater key (the key id of -PublicKey, by default
the key the app trusts, in src/Tack.Core/Updates/UpdateTrust.cs). With
-AssetNames, each installer and its .sig must be among the release's assets.

It reads the signatures but cannot verify them (no Ed25519 here); the
updaters do.

.EXAMPLE
./scripts/release/Test-LatestJson.ps1 -Path latest.json -Version 0.3.0 -Tag v0.3.0 -Repository prajjwalyd/tack
#>
param(
    [Parameter(Mandatory)] [string] $Path,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Tag,
    [Parameter(Mandatory)] [string] $Repository,
    [string] $PublicKey,
    [string[]] $AssetNames
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TauriSignature.ps1')

if (-not $PublicKey) {
    $root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $trust = Get-Content -Raw (Join-Path $root 'src/Tack.Core/Updates/UpdateTrust.cs')
    if ($trust -notmatch 'PublicKey\s*=\s*"([^"]+)"') {
        throw 'UpdateTrust.cs has no PublicKey.'
    }
    $PublicKey = $Matches[1]
}
$keyId = Read-TauriPublicKeyId $PublicKey

$problems = New-Object System.Collections.Generic.List[string]
$feed = Get-Content -Raw $Path | ConvertFrom-Json

$keys = @($feed.PSObject.Properties.Name)
if (($keys -join ',') -ne 'version,notes,pub_date,platforms') {
    $problems.Add("The top-level keys are '$($keys -join ', ')', not 0.2's 'version, notes, pub_date, platforms'.")
}
if ($feed.version -ne $Version) {
    $problems.Add("version is '$($feed.version)', not '$Version'.")
}
if ($feed.notes -isnot [string]) {
    $problems.Add('notes is not a string.')
}
# pwsh reads an ISO date as a DateTime already; Windows PowerShell keeps the text.
$date = [DateTimeOffset]::MinValue
if ($feed.pub_date -isnot [DateTime] -and -not [DateTimeOffset]::TryParse("$($feed.pub_date)", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref] $date)) {
    $problems.Add("pub_date '$($feed.pub_date)' is not a date.")
}

$prefix = "https://github.com/$Repository/releases/download/$Tag/"
$builds = [ordered]@{ 'windows-x86_64' = 'x64'; 'windows-aarch64' = 'arm64' }
$platforms = @($feed.platforms.PSObject.Properties.Name)
$expected = @($builds.Keys | ForEach-Object { $_; "$_-nsis" })
if (($platforms -join ',') -ne ($expected -join ',')) {
    $problems.Add("The platforms are '$($platforms -join ', ')', not '$($expected -join ', ')'.")
}

foreach ($platform in $builds.Keys) {
    $entry = $feed.platforms.$platform
    $nsis = $feed.platforms."$platform-nsis"
    if (-not $entry) {
        $problems.Add("$platform is missing.")
        continue
    }
    if (-not $nsis -or $nsis.url -ne $entry.url -or $nsis.signature -ne $entry.signature) {
        $problems.Add("$platform-nsis is not the same as $platform.")
    }
    $installer = "Tack_${Version}_$($builds[$platform])-setup.exe"
    if ($entry.url -ne "$prefix$installer") {
        $problems.Add("$platform's url is '$($entry.url)', not '$prefix$installer'.")
    }
    if ($AssetNames -and ($AssetNames -notcontains $installer -or $AssetNames -notcontains "$installer.sig")) {
        $problems.Add("The release lacks $installer or $installer.sig.")
    }
    try {
        $signature = Read-TauriSignature $entry.signature
    } catch {
        $problems.Add("$platform's signature: $($_.Exception.Message)")
        continue
    }
    # The updaters refuse a signature whose version differs from the feed's.
    if ($signature.Version -ne $Version) {
        $problems.Add("$platform's signature is for version '$($signature.Version)', not '$Version'.")
    }
    if ($signature.File -ne $installer) {
        $problems.Add("$platform's signature is for '$($signature.File)', not '$installer'.")
    }
    if ($signature.KeyId -ne $keyId) {
        $problems.Add("$platform is signed with key $($signature.KeyId), not the updater key $keyId.")
    }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) {
        if ($env:GITHUB_ACTIONS) {
            Write-Host "::error::$problem"
        } else {
            Write-Host $problem
        }
    }
    throw "latest.json has $($problems.Count) problem(s)."
}
Write-Host "latest.json is right: version $Version, both builds, signed with key $keyId."
