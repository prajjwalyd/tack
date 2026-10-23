<#
.SYNOPSIS
Answers the uninstaller's windows as a person would: ticks "Delete the
application data", then presses Uninstall, Yes and OK as they come.
Dot-source it and call Invoke-UninstallerDialogs.

.DESCRIPTION
Inno Setup's windows, its message boxes included, are plain Win32 dialogs
whose check boxes and buttons are BUTTON windows. A check box gets a
BM_CLICK, as from the mouse; a button's click is sent to its dialog the way
the button itself tells it, WM_COMMAND with the button's id. Neither needs
the mouse or the focus.
#>
if (-not ('TackUpgrade.Dialogs' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace TackUpgrade
{
    public static class Dialogs
    {
        delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

        const uint WM_COMMAND = 0x0111, BM_GETCHECK = 0x00F0, BM_CLICK = 0x00F5;

        public static List<IntPtr> TopWindows()
        {
            var found = new List<IntPtr>();
            EnumWindows((h, _) => { if (IsWindowVisible(h)) found.Add(h); return true; }, IntPtr.Zero);
            return found;
        }

        public static List<IntPtr> Children(IntPtr parent)
        {
            var found = new List<IntPtr>();
            EnumChildWindows(parent, (h, _) => { if (IsWindowVisible(h)) found.Add(h); return true; }, IntPtr.Zero);
            return found;
        }

        public static string Text(IntPtr hwnd)
        {
            var text = new StringBuilder(1024);
            GetWindowTextW(hwnd, text, text.Capacity);
            return text.ToString().Replace("&", "");
        }

        public static bool IsButton(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            GetClassNameW(hwnd, name, name.Capacity);
            var cls = name.ToString();
            // Win32's (message boxes), Inno's and WinForms' buttons and check boxes.
            return cls.Equals("Button", StringComparison.OrdinalIgnoreCase) || cls == "TNewButton" || cls == "TNewCheckBox" || cls.Contains(".BUTTON.");
        }

        public static bool IsChecked(IntPtr hwnd)
        {
            return SendMessageW(hwnd, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)1;
        }

        // A click on a check box: it ticks itself (or has its dialog tick it) as for the mouse.
        public static void Tick(IntPtr hwnd)
        {
            PostMessageW(hwnd, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }

        // What a click on a button sends its dialog. Posted: the dialog may
        // open another (a message box) before it is done with it.
        public static void Press(IntPtr hwnd)
        {
            var id = GetDlgCtrlID(hwnd) & 0xFFFF;
            PostMessageW(GetParent(hwnd), WM_COMMAND, (IntPtr)id, hwnd);
        }
    }
}
'@
}
$Dialogs = [TackUpgrade.Dialogs]

# Tack's question ("Uninstall Tack") and Inno's message boxes and progress
# ("Tack Uninstall"); every other window on the desktop is left alone.
$UninstallerTitles = '^(Uninstall Tack|Tack Uninstall)$'

# Ticks the box if it is there, then presses the first of Uninstall, Yes or
# OK. Returns what was answered, or $null when the window has none of them
# or its box is not ticked yet.
function Answer([IntPtr] $Window) {
    # Read before anything is pressed: the window closes once it is.
    $title = $Dialogs::Text($Window)
    $children = $Dialogs::Children($Window)
    $buttons = @($children | Where-Object { $Dialogs::IsButton($_) })
    $ticked = ''
    $box = $buttons | Where-Object { $Dialogs::Text($_) -eq 'Delete the application data' } | Select-Object -First 1
    if ($box) {
        if (-not $Dialogs::IsChecked($box)) {
            # The click is posted to the window: look again next time round.
            $Dialogs::Tick($box)
            return $null
        }
        $ticked = ' [Delete the application data: ticked]'
    }
    foreach ($wanted in 'Uninstall', 'Yes', 'OK') {
        $button = $buttons | Where-Object { $Dialogs::Text($_) -eq $wanted } | Select-Object -First 1
        if (-not $button) { continue }
        $text = @($children | Where-Object { -not $Dialogs::IsButton($_) } | ForEach-Object { $Dialogs::Text($_) } | Where-Object { $_ }) -join ' '
        $Dialogs::Press($button)
        return "$title`: $text$ticked -> $wanted"
    }
    $null
}

function Get-OpenDialogs([string] $Titles) {
    @($Dialogs::TopWindows() | Where-Object { $Dialogs::Text($_) -match $Titles })
}

# Answers every window titled $Titles until $Process has exited and none is
# left open, each once. Returns the answers in order; throws after $Seconds.
function Invoke-UninstallerDialogs([Diagnostics.Process] $Process, [string] $Titles = $UninstallerTitles, [int] $Seconds = 120) {
    $answers = New-Object Collections.Generic.List[string]
    # A window can still be listed for a moment after it was answered.
    $answered = New-Object Collections.Generic.HashSet[long]
    $clock = [Diagnostics.Stopwatch]::StartNew()
    # The first copy of the uninstaller can exit before its last message box is closed.
    while ((-not $Process.HasExited -or (Get-OpenDialogs $Titles).Count -gt 0) -and $clock.Elapsed.TotalSeconds -lt $Seconds) {
        foreach ($window in Get-OpenDialogs $Titles) {
            if ($answered.Contains($window.ToInt64())) { continue }
            $answer = Answer $window
            if ($answer) {
                Write-Host "  answered $answer"
                $answers.Add($answer)
                [void]$answered.Add($window.ToInt64())
            }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $Process.HasExited -or (Get-OpenDialogs $Titles).Count -gt 0) {
        Get-OpenDialogs $Titles | ForEach-Object {
            $window = $_
            Write-Host "  still open: '$($Dialogs::Text($window))' with $(@($Dialogs::Children($window) | ForEach-Object { "'$($Dialogs::Text($_))'" }) -join ', ')"
        }
        throw "The uninstaller did not finish within $Seconds s."
    }
    , $answers.ToArray()
}
