<#
.SYNOPSIS
Fails when a C# or XAML file under src/ or tests/ is longer than the limit.
A file that long is doing too much: split it into smaller types, one per file.

.EXAMPLE
./scripts/check-file-length.ps1
#>
param([int] $Limit = 300)

$root = Split-Path $PSScriptRoot -Parent
$folders = 'src', 'tests' | ForEach-Object { Join-Path $root $_ } | Where-Object { Test-Path $_ }
$long = Get-ChildItem $folders -Recurse -File -Include *.cs, *.xaml |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
        [pscustomobject]@{
            Path  = $_.FullName.Substring($root.Length + 1)
            Lines = @(Get-Content $_.FullName).Count
        }
    } |
    Where-Object Lines -gt $Limit

if ($long) {
    $long | Sort-Object Lines -Descending | ForEach-Object { Write-Host "$($_.Path): $($_.Lines) lines (limit $Limit)" }
    exit 1
}
Write-Host "Every C# and XAML file is $Limit lines or fewer."
