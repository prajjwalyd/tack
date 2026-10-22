# Renders the print art in assets/board: the shadows the board draws as nine
# grids, the button and badge rims, the pins (five colours, brass, the contact
# shadow) and the paper's fibre. Each piece is drawn by headless Microsoft
# Edge from print-art.css (0.2's own rules) at 100, 150, 200 and 250 percent,
# light and dark where the colours differ, then cut out of the sheet. The
# fibre is 0.2's 64 px tile, laid 8 by 8 (no tiling brush on the board).
# Run it again only when the art changes; its output is committed.
param(
    [string]$Out = "$PSScriptRoot\..\..\assets\board",
    [string]$Edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force $Out | Out-Null

# Every cell's origin is a multiple of 4 and its size a multiple of 2 (CSS px),
# so it cuts out on whole pixels at every scale. Themed pieces get -light or -dark.
$pin = { param($kind, $color) "<div class='pin $kind' data-c='$color'><span class='pin-head'></span><span class='pin-brass'></span></div>" }
$cells = @(
    @{ Name = "rest"; X = 0; Y = 0; W = 104; H = 104; Themed = $true; Html = "<div class='box rest' style='left:24px;top:24px;width:56px;height:56px'></div>" }
    @{ Name = "note"; X = 108; Y = 0; W = 112; H = 112; Themed = $true; Html = "<div class='box note' style='left:24px;top:24px;width:64px;height:64px'></div>" }
    @{ Name = "lift"; X = 224; Y = 0; W = 176; H = 176; Themed = $true; Html = "<div class='box lift' style='left:40px;top:40px;width:96px;height:96px'></div>" }
    @{ Name = "button"; X = 0; Y = 180; W = 42; H = 42; Themed = $false; Html = "<div class='box button' style='left:12px;top:12px;width:18px;height:18px'></div>" }
    @{ Name = "badge"; X = 48; Y = 180; W = 60; H = 60; Themed = $false; Html = "<div class='box badge' style='left:16px;top:16px;width:28px;height:28px'></div>" }
    @{ Name = "pin-shadow"; X = 112; Y = 180; W = 28; H = 28; Themed = $true; Html = (& $pin "contact" "graphite") }
    @{ Name = "brass"; X = 144; Y = 180; W = 28; H = 28; Themed = $true; Html = (& $pin "brass" "graphite") }
)
$x = 176
foreach ($color in "terracotta", "sage", "ink", "sand", "graphite") {
    $cells += @{ Name = "pin-$color"; X = $x; Y = 180; W = 28; H = 28; Themed = $true; Html = (& $pin "head" $color) }
    $x += 32
}

$work = Join-Path ([IO.Path]::GetTempPath()) "tack-board-assets-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null
Copy-Item "$PSScriptRoot\print-art.css" $work
try {
    foreach ($theme in "light", "dark") {
        $body = ($cells | ForEach-Object { "<div class='cell' style='left:$($_.X)px;top:$($_.Y)px;width:$($_.W)px;height:$($_.H)px'>$($_.Html)</div>" }) -join "`n"
        $html = "<!doctype html><html><head><link rel='stylesheet' href='print-art.css'><style>:root{color-scheme:$theme}</style></head><body>$body</body></html>"
        $page = Join-Path $work "$theme.html"
        Set-Content -LiteralPath $page -Value $html -Encoding UTF8
        foreach ($percent in 100, 150, 200, 250) {
            $scale = $percent / 100
            $shot = Join-Path $work "$theme-$percent.png"
            $arguments = @(
                "--headless", "--disable-gpu", "--no-first-run", "--hide-scrollbars",
                "`"--user-data-dir=$work\profile`"", "--force-device-scale-factor=$scale",
                "--default-background-color=00000000", "--window-size=400,244",
                "`"--screenshot=$shot`"", ([Uri]$page).AbsoluteUri)
            Start-Process -FilePath $Edge -ArgumentList $arguments -Wait -WindowStyle Hidden
            $sheet = [System.Drawing.Bitmap]::new($shot)
            try {
                foreach ($cell in $cells) {
                    if (-not $cell.Themed -and $theme -eq "dark") { continue }
                    $name = if ($cell.Themed) { "$($cell.Name)-$theme" } else { $cell.Name }
                    $rect = [System.Drawing.Rectangle]::new($cell.X * $scale, $cell.Y * $scale, $cell.W * $scale, $cell.H * $scale)
                    $piece = $sheet.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                    $piece.Save((Join-Path $Out "$name-$percent.png"), [System.Drawing.Imaging.ImageFormat]::Png)
                    $piece.Dispose()
                }
            }
            finally { $sheet.Dispose() }
        }
    }

    $tile = [System.Drawing.Bitmap]::new("$PSScriptRoot\fibre-64.png")
    $fibre = [System.Drawing.Bitmap]::new(512, 512, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($fibre)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    for ($ty = 0; $ty -lt 512; $ty += 64) { for ($tx = 0; $tx -lt 512; $tx += 64) { $g.DrawImage($tile, [System.Drawing.Rectangle]::new($tx, $ty, 64, 64)) } }
    $g.Dispose()
    $fibre.Save((Join-Path $Out "fibre.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $fibre.Dispose()
    $tile.Dispose()
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem $Out -Filter *.png | Measure-Object Length -Sum | ForEach-Object { "$($_.Count) pictures, $($_.Sum) bytes in $Out" }
