# Bakes the brushed grain of the board's metal frame into two pictures:
# src/Tack.App/Assets/Board/chrome/grain-side.png (12 x 1024, a side rail's, brushed down)
# and grain-bottom.png (1024 x 12, the bottom rail's, brushed across). Each is
# the same pair of seeded fractal-noise fields Tack 0.2 drew: fine white
# streaks over fine dark ones, each with an alpha, so they lie over any finish.
# The board draws them at half size (6 px wide rails), so they are sharp at
# 200 % scaling.
#
#   powershell -NoProfile -File scripts/board-chrome/grain.ps1
#   powershell -NoProfile -File scripts/board-chrome/grain.ps1 -Out <folder>
#
# Microsoft Edge renders them, headless, with a clear background. The noise is
# seeded, so the same Edge always writes the same pictures. Nothing leaves the machine.
param(
    [string]$Out = "$PSScriptRoot\..\..\src\Tack.App\Assets\Board\chrome",
    [string]$Edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $Edge)) { throw "Microsoft Edge is not at $Edge; pass -Edge" }
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force $Out | Out-Null
$work = Join-Path ([IO.Path]::GetTempPath()) "tack-grain-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null

# One rail's grain: a light field and a dark one, stretched along the rail by
# their base frequencies (fine across it, long along it).
function Grain([int]$Width, [int]$Height, [string]$Light, [string]$Dark) {
    "<svg xmlns='http://www.w3.org/2000/svg' width='$Width' height='$Height'>" +
    "<filter id='f' x='0' y='0' width='100%25' height='100%25'><feTurbulence type='fractalNoise' baseFrequency='$Light' numOctaves='3' seed='2' stitchTiles='stitch'/>" +
    "<feColorMatrix values='0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0.6 0 0 0 -0.28'/></filter><rect width='100%25' height='100%25' filter='url(%23f)'/>" +
    "<filter id='g' x='0' y='0' width='100%25' height='100%25'><feTurbulence type='fractalNoise' baseFrequency='$Dark' numOctaves='3' seed='9' stitchTiles='stitch'/>" +
    "<feColorMatrix values='0 0 0 0 0  0 0 0 0 0  0 0 0 0 0  0.5 0 0 0 -0.24'/></filter><rect width='100%25' height='100%25' filter='url(%23g)'/></svg>"
}

function Bake([string]$Name, [int]$Width, [int]$Height, [string]$Light, [string]$Dark) {
    $page = Join-Path $work "$Name.html"
    $picture = Join-Path $work "$Name.png"
    $svg = Grain $Width $Height $Light $Dark
    Set-Content -LiteralPath $page -Encoding utf8 -Value (
        "<!doctype html><html><head><style>html,body{margin:0;background:transparent;overflow:hidden}img{display:block}</style></head>" +
        "<body><img src=`"data:image/svg+xml,$svg`"></body></html>")
    # Start-Process joins the arguments with spaces, so paths are quoted.
    $arguments = @(
        "--headless", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1", "--force-color-profile=srgb",
        "--default-background-color=00000000", "--user-data-dir=`"$work\profile`"", "--window-size=$Width,$Height",
        "--screenshot=`"$picture`"", ([Uri]$page).AbsoluteUri)
    Start-Process -FilePath $Edge -ArgumentList $arguments -Wait -WindowStyle Hidden
    if (-not (Test-Path -LiteralPath $picture)) { throw "Edge did not write $Name" }
    Copy-Item -LiteralPath $picture -Destination (Join-Path $Out "$Name.png") -Force
    "${Name}: $Width x $Height px, $([Math]::Round((Get-Item (Join-Path $Out "$Name.png")).Length / 1KB, 1)) KB"
}

try {
    Bake "grain-side" 12 1024 "0.45 0.0025" "0.5 0.003"
    Bake "grain-bottom" 1024 12 "0.0025 0.45" "0.003 0.5"
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
