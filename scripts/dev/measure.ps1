<#
.SYNOPSIS
Measures a Release build of Tack: start to the tray, the first reveal,
memory at rest, shown and tucked, wake-ups while tucked and while shown, and
the CPU for 20 reveals. Only Tack's own process and windows are looked at; nothing on the
screen is captured.

.DESCRIPTION
Runs against a throwaway copy of the development data (%LOCALAPPDATA%\Tack.Dev),
so the board has prints, never the real board. Release builds always sweep
leftovers older than 8 days to the Recycle Bin, so every copied file is first
dated now, which leaves nothing to sweep; the copy's update check is turned
off so no network request lands in the numbers. The board is shown and hidden
by posting the toggle shortcut's WM_HOTKEY to Tack's own host window, so no
keys are pressed anywhere.

.EXAMPLE
./measure.ps1 -Exe C:\temp\tack-publish\Tack.exe
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $Settle = 10,
    # The compositor gives back its buffers some 30 s after the board tucks.
    [int] $TuckSettle = 35,
    [int] $Reveals = 20
)
$ErrorActionPreference = 'Stop'

Add-Type -Namespace TackMeasure -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
[DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
'@
$WM_HOTKEY = 0x0312
# HotkeyAction.Toggle.
$TOGGLE = [IntPtr]1

if (Get-CimInstance Win32_Process -Filter "Name='Tack.exe'") {
    throw 'A Tack is already running; quit it first (it would hold the single-instance mutex).'
}

# The throwaway copy.
$scratch = Join-Path ([IO.Path]::GetTempPath()) "tack-measure-$([guid]::NewGuid().ToString('N'))"
$dev = Join-Path $env:LOCALAPPDATA 'Tack.Dev'
if (Test-Path -LiteralPath $dev) { Copy-Item -LiteralPath $dev -Destination $scratch -Recurse } else { New-Item -ItemType Directory $scratch | Out-Null }
$now = Get-Date
Get-ChildItem -LiteralPath $scratch -Recurse -File | ForEach-Object { $_.LastWriteTime = $now }
$board = Join-Path $scratch 'Roaming\board.json'
if (Test-Path -LiteralPath $board) {
    $json = Get-Content -LiteralPath $board -Raw | ConvertFrom-Json
    $json | Add-Member -NotePropertyName updates -NotePropertyValue ([pscustomobject]@{ check = $false }) -Force
    [IO.File]::WriteAllText($board, ($json | ConvertTo-Json -Depth 20))
    (Get-Item -LiteralPath $board).LastWriteTime = $now
}

function Sample($process, $state) {
    $process.Refresh()
    $perf = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$($process.Id)"
    [pscustomobject]@{
        State              = $state
        'Task Manager MB'  = [math]::Round($perf.WorkingSetPrivate / 1MB, 1)
        'Private bytes MB' = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        'CPU s'            = [math]::Round($process.TotalProcessorTime.TotalSeconds, 2)
        Threads            = $process.Threads.Count
    }
}

# Context switches per second across the process's threads, over a few seconds: its wake-ups.
function WakeUps($process, [int] $seconds = 5) {
    $before = @{}
    Get-CimInstance Win32_PerfRawData_PerfProc_Thread -Filter "IDProcess=$($process.Id)" | ForEach-Object { $before[$_.IDThread] = [uint64]$_.ContextSwitchesPersec }
    Start-Sleep -Seconds $seconds
    $total = [uint64]0
    Get-CimInstance Win32_PerfRawData_PerfProc_Thread -Filter "IDProcess=$($process.Id)" | ForEach-Object {
        if ($before.ContainsKey($_.IDThread)) { $total += [uint64]$_.ContextSwitchesPersec - $before[$_.IDThread] }
    }
    [math]::Round($total / $seconds, 1)
}

# Tack's visible WinUI windows: the board, while it is down.
function BoardVisible([uint32] $id) {
    $hwnd = [IntPtr]::Zero
    while (($hwnd = [TackMeasure.Win32]::FindWindowExW([IntPtr]::Zero, $hwnd, 'WinUIDesktopWin32WindowClass', [NullString]::Value)) -ne [IntPtr]::Zero) {
        $owner = [uint32]0
        [void][TackMeasure.Win32]::GetWindowThreadProcessId($hwnd, [ref]$owner)
        if ($owner -eq $id -and [TackMeasure.Win32]::IsWindowVisible($hwnd)) { return $true }
    }
    $false
}

$env:TACK_HOME = $scratch
# Started from a packaged terminal, Tack would start itself again outside the
# package first; started at sign-in or from the Start menu it does not, so the
# start is measured without that.
$env:TACK_STAY_IN_PACKAGE = '1'
$clock = [Diagnostics.Stopwatch]::StartNew()
$launcher = Start-Process (Resolve-Path $Exe).Path -PassThru
$hwnd = [IntPtr]::Zero
while ($hwnd -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 20) {
    Start-Sleep -Milliseconds 10
    $hwnd = [TackMeasure.Win32]::FindWindowW('TackHost', [NullString]::Value)
}
$toTray = $clock.ElapsedMilliseconds
if ($hwnd -eq [IntPtr]::Zero) { Stop-Process $launcher -Force -ErrorAction SilentlyContinue; throw 'Tack never created its host window.' }
# Tack may start itself again outside the package: measure whoever owns the host window.
$tackId = [uint32]0
[void][TackMeasure.Win32]::GetWindowThreadProcessId($hwnd, [ref]$tackId)
$process = Get-Process -Id $tackId
function Toggle { [void][TackMeasure.Win32]::PostMessageW($hwnd, $WM_HOTKEY, $TOGGLE, [IntPtr]::Zero) }

try {
    Start-Sleep $Settle
    $rows = @(Sample $process 'At rest, board not shown yet')
    $restWake = WakeUps $process

    $clock.Restart()
    Toggle
    while (-not (BoardVisible $tackId) -and $clock.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 2 }
    $firstReveal = $clock.ElapsedMilliseconds
    Start-Sleep 3
    $rows += Sample $process 'Board shown'
    $shownWake = WakeUps $process

    Toggle
    Start-Sleep $TuckSettle
    $rows += Sample $process 'Tucked away, settled'
    $tuckedWake = WakeUps $process 10

    $cpuBefore = $process.TotalProcessorTime
    for ($i = 0; $i -lt $Reveals; $i++) {
        Toggle; Start-Sleep -Milliseconds 800
        Toggle; Start-Sleep -Milliseconds 500
    }
    Start-Sleep 2
    $process.Refresh()
    $revealCpu = ($process.TotalProcessorTime - $cpuBefore).TotalSeconds
    Start-Sleep $TuckSettle
    $rows += Sample $process "After $Reveals more reveals, settled"
}
finally {
    Stop-Process -Id $tackId -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

$rows | Format-Table -AutoSize
[pscustomobject]@{
    'Start to host window (ms)'      = $toTray
    'First reveal to visible (ms)'   = $firstReveal
    'Wake-ups/s at rest'             = $restWake
    'Wake-ups/s shown'               = $shownWake
    'Wake-ups/s tucked'              = $tuckedWake
    "CPU s for $Reveals reveals"     = [math]::Round($revealCpu, 2)
} | Format-List
