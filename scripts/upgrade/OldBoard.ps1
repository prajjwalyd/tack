<#
.SYNOPSIS
Writes a board as a person's Tack 0.2 would have one: a capture, a note and a
kept picture of their own, every setting changed from its default, and an
approved phone. Dot-source it after Common.ps1, whose folders it writes to,
and call Write-OldBoard.

.DESCRIPTION
Everything is dated now, so no Tack ages a print out or sweeps a file, and
the update check is off, so no Tack asks GitHub for a release.
#>
Add-Type -AssemblyName System.Drawing

function Write-Picture([string] $Path, [string] $Color) {
    New-Item -ItemType Directory -Force (Split-Path $Path) | Out-Null
    $bitmap = New-Object Drawing.Bitmap 800, 500
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::FromName($Color))
    $graphics.FillRectangle([Drawing.Brushes]::White, 40, 40, 320, 120)
    $graphics.Dispose()
    $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

function Write-OldBoard([bool] $PhoneOn = $true) {
    $utf8 = New-Object Text.UTF8Encoding $false
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $stamp = (Get-Date).AddMinutes(-3).ToString('yyyy-MM-dd HHmmss')
    $capture = Join-Path $CapturesDir "Screenshot $stamp.png"
    $note = Join-Path $NotesDir "Note $stamp.txt"

    Write-Picture $capture 'SteelBlue'
    Write-Picture $OwnPicture 'DarkOrange'
    New-Item -ItemType Directory -Force $NotesDir | Out-Null
    [IO.File]::WriteAllText($note, "Upgrade check`r`nThis note was pinned in Tack 0.2.", $utf8)

    # The shape of 0.2's board.json (tests/fixtures/board-v0.2.json).
    $board = [ordered]@{
        version        = 3
        prints         = @(
            [ordered]@{ path = $OwnPicture; capture = $false; pinnedAt = $now - 240000; kept = $true; keptAt = $now - 60000 }
            [ordered]@{ path = $note; kind = 'note'; capture = $true; pinnedAt = $now - 120000; kept = $false; keptAt = $null }
            [ordered]@{ path = $capture; capture = $true; pinnedAt = $now - 180000; kept = $false; keptAt = $null }
        )
        sound          = $false
        edgeReveal     = $false
        reveal         = 'slow'
        appearance     = [ordered]@{ felt = 'navy'; frame = 'brass'; paper = 'butter' }
        updates        = [ordered]@{ check = $false }
        snipTipShown   = $true
        toggleShortcut = 'Ctrl+Alt+T'
        pinShortcut    = 'Ctrl+Alt+P'
        phone          = [ordered]@{
            on      = $PhoneOn
            devices = @([ordered]@{ key = 'dXBncmFkZS1jaGVjaw=='; name = 'Upgrade check phone'; approvedAt = $now - 300000 })
        }
    }
    New-Item -ItemType Directory -Force $TackRoaming | Out-Null
    [IO.File]::WriteAllText($BoardFile, (ConvertTo-Json $board -Depth 10), $utf8)
}
