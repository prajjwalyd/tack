<#
.SYNOPSIS
Reads updater signatures and keys in the Tauri format: the base64 of a
minisign .minisig or .pub file. Dot-sourced by the other release scripts.

.DESCRIPTION
A signature decodes to four lines:

    untrusted comment: signature from tauri secret key
    <base64: algorithm (2 bytes), key id (8), signature (64)>
    trusted comment: timestamp:<unix time>\tfile:<file name>\tversion:<version>
    <base64: global signature over the signature and the trusted comment>

The trusted comment is covered by the signature, so its version is what the
updater (0.2's with requireSignedVersion, and the C# one) compares with the
version in latest.json. A public key decodes to two lines: a comment and the
base64 of algorithm (2 bytes), key id (8) and key (32).

This reads; it does not verify (PowerShell has no Ed25519). The updaters do.
#>

function ConvertFrom-TauriBase64([string] $Text) {
    try {
        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Text.Trim()))
    } catch {
        throw 'Not base64: a Tauri signature or key is the base64 of the minisign file.'
    }
}

function Get-KeyIdHex([string] $Base64Line) {
    $bytes = [Convert]::FromBase64String($Base64Line.Trim())
    # minisign stores the key id little-endian and prints it reversed.
    $id = $bytes[2..9]
    [Array]::Reverse($id)
    -join ($id | ForEach-Object { $_.ToString('X2') })
}

<# The key id, and the fields of the trusted comment, of a Tauri signature. #>
function Read-TauriSignature([string] $Signature) {
    $lines = @((ConvertFrom-TauriBase64 $Signature) -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($lines.Count -ne 4 -or $lines[0] -notlike 'untrusted comment:*' -or $lines[2] -notlike 'trusted comment: *') {
        throw 'Not a minisign signature.'
    }
    $fields = @{}
    foreach ($field in $lines[2].Substring('trusted comment: '.Length) -split "`t") {
        $name, $value = $field -split ':', 2
        $fields[$name] = $value
    }
    [pscustomobject]@{
        KeyId     = Get-KeyIdHex $lines[1]
        Timestamp = $fields['timestamp']
        File      = $fields['file']
        Version   = $fields['version']
    }
}

<# The key id of a Tauri public key (the base64 of a minisign .pub file). #>
function Read-TauriPublicKeyId([string] $PublicKey) {
    $lines = @((ConvertFrom-TauriBase64 $PublicKey) -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($lines.Count -ne 2 -or $lines[0] -notlike 'untrusted comment:*') {
        throw 'Not a minisign public key.'
    }
    Get-KeyIdHex $lines[1]
}
