<#
.SYNOPSIS
What the upgrade check's scripts share: where Tack keeps its things, finding
a running Tack, a snapshot of the person's data, and checks that fail the
run. Dot-source it: . "$PSScriptRoot/Common.ps1"

.DESCRIPTION
The upgrade check installs Tack 0.2, then this Tack over it, then uninstalls
it, for the current user. It replaces whatever Tack is installed and can
delete its data, so it runs only on a throwaway machine: a GitHub Actions
runner, or a virtual machine where TACK_THROWAWAY_MACHINE=1 is set by hand.
#>
$ErrorActionPreference = 'Stop'

if ($env:GITHUB_ACTIONS -ne 'true' -and $env:TACK_THROWAWAY_MACHINE -ne '1') {
    throw 'The upgrade check replaces the installed Tack and may delete its data. Run it only on a throwaway machine (set TACK_THROWAWAY_MACHINE=1 there).'
}

$TackRoaming = Join-Path $env:APPDATA 'Tack'
$TackLocal = Join-Path $env:LOCALAPPDATA 'Tack'
$BoardFile = Join-Path $TackRoaming 'board.json'
$CapturesDir = Join-Path $TackLocal 'Captures'
$NotesDir = Join-Path $TackLocal 'Notes'
# A print that is not a capture: a picture of the person's own, kept.
$OwnPicture = Join-Path $env:USERPROFILE 'Pictures\Screenshots\Upgrade check.png'

$UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$NewUninstallKey = "$UninstallKey\{61E19669-5E3C-4153-A9BF-1D36EFE9BEA9}_is1"
$OldUninstallKey = "$UninstallKey\Tack"
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Tack.lnk'

if (-not ('TackUpgrade.Win32' -as [type])) {
    Add-Type -Namespace TackUpgrade -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string title);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
'@
}

$script:Failures = 0

# A check: passes quietly, or fails the step at its end (Complete-Checks).
# Anything PowerShell counts as true passes: a found item, a non-empty list.
function Assert($Condition, [string] $What) {
    if ($Condition) {
        Write-Host "  ok   $What"
    }
    else {
        Write-Host "::error::$What"
        $script:Failures++
    }
}

function Complete-Checks([string] $Stage) {
    if ($script:Failures -gt 0) {
        throw "$Stage`: $($script:Failures) check(s) failed."
    }
    Write-Host "$Stage`: every check passed."
}

# Polls Condition every half second until it is true or Seconds pass.
function Wait-Until([scriptblock] $Condition, [int] $Seconds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 500
    }
    [bool](& $Condition)
}

function Test-SamePath([string] $A, [string] $B) {
    if (-not $A -or -not $B) { return $false }
    [IO.Path]::GetFullPath($A).TrimEnd('\') -ieq [IO.Path]::GetFullPath($B).TrimEnd('\')
}

# Every process named tack.exe or Tack.exe, with where it runs from.
function Get-TackProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name = 'tack.exe'" | ForEach-Object {
        [pscustomobject]@{ Id = [int]$_.ProcessId; Path = [string]$_.ExecutablePath }
    })
}

# The process that owns the C# Tack's host window (0.2 has none), or $null.
function Get-TackHost {
    $hwnd = [TackUpgrade.Win32]::FindWindowW('TackHost', [NullString]::Value)
    if ($hwnd -eq [IntPtr]::Zero) { return $null }
    $id = [uint32]0
    [void][TackUpgrade.Win32]::GetWindowThreadProcessId($hwnd, [ref]$id)
    Get-TackProcesses | Where-Object Id -eq $id | Select-Object -First 1
}

function Get-RunValue {
    (Get-ItemProperty -Path $RunKey -Name 'Tack' -ErrorAction SilentlyContinue).Tack
}

function Get-FileHashes([string[]] $Folders) {
    $hashes = [ordered]@{}
    foreach ($folder in $Folders) {
        if (-not (Test-Path -LiteralPath $folder)) { continue }
        Get-ChildItem -LiteralPath $folder -File | Sort-Object Name | ForEach-Object {
            $hashes[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
    $hashes
}

# The person's data as a comparable record: each print as one line, each
# setting as its JSON, and a hash of every file the board shows.
function Get-Snapshot {
    $board = [IO.File]::ReadAllText($BoardFile) | ConvertFrom-Json
    $prints = @($board.prints | ForEach-Object {
        "$($_.path) | kind=$($_.kind) capture=$($_.capture) pinnedAt=$($_.pinnedAt) kept=$($_.kept) keptAt=$($_.keptAt)"
    } | Sort-Object)
    $settings = [ordered]@{}
    foreach ($property in $board.PSObject.Properties) {
        if ($property.Name -ne 'prints') {
            $settings[$property.Name] = ConvertTo-Json $property.Value -Compress -Depth 20
        }
    }
    $files = Get-FileHashes @($CapturesDir, $NotesDir, (Split-Path $OwnPicture))
    [pscustomobject]@{ Prints = $prints; Settings = $settings; Files = $files }
}

# Writes a snapshot, with anything else worth remembering about the machine
# then (Facts), for the later steps.
function Save-Snapshot([string] $Path, [hashtable] $Facts = @{}) {
    $snapshot = Get-Snapshot
    $snapshot | Add-Member -NotePropertyName Facts -NotePropertyValue ([pscustomobject]$Facts)
    [IO.File]::WriteAllText($Path, (ConvertTo-Json $snapshot -Depth 20))
    $snapshot
}

function Read-Snapshot([string] $Path) {
    [IO.File]::ReadAllText($Path) | ConvertFrom-Json
}

# Every print and setting of Before is still there, unchanged, and so is
# every file. Settings a newer Tack adds are allowed.
function Assert-SameData($Before) {
    Assert (Test-Path -LiteralPath $BoardFile) "board.json is still at $BoardFile"
    if (-not (Test-Path -LiteralPath $BoardFile)) { return }
    $now = Get-Snapshot
    $lost = @($Before.Prints | Where-Object { $now.Prints -notcontains $_ })
    $added = @($now.Prints | Where-Object { $Before.Prints -notcontains $_ })
    Assert ($lost.Count -eq 0 -and $added.Count -eq 0) "board.json lists the same $(@($Before.Prints).Count) prints (lost: $($lost -join '; ') added: $($added -join '; '))"
    foreach ($setting in $Before.Settings.PSObject.Properties) {
        $after = $now.Settings[$setting.Name]
        Assert ($after -ceq $setting.Value) "setting '$($setting.Name)' is unchanged ($($setting.Value) -> $after)"
    }
    foreach ($file in $Before.Files.PSObject.Properties) {
        Assert ($now.Files[$file.Name] -eq $file.Value) "$($file.Name) is unchanged"
    }
}

# Setup's newest log in %TEMP%, for a run that went wrong.
function Show-SetupLog {
    $log = Get-ChildItem -LiteralPath $env:TEMP -Filter 'Setup Log *.txt' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime | Select-Object -Last 1
    if ($log) {
        Write-Host "--- $($log.Name)"
        Get-Content -LiteralPath $log.FullName | Write-Host
    }
}
