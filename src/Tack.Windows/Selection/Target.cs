using Tack.Windows.Clipboard;
using Tack.Windows.Foreground;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Tack.Windows.Selection;

/// <summary>
/// The window the pin shortcut was pressed over, taken the moment it fired.
/// The copy keys go only to it, and only its app's copy is read.
/// </summary>
public sealed class Target
{
    /// <summary>
    /// Terminals, by window class. Ctrl+C there interrupts the running command,
    /// so they get Ctrl+Insert, which copies the selection in all of them.
    /// </summary>
    static readonly string[] s_terminals =
    [
        "ConsoleWindowClass",            // the console (conhost)
        "CASCADIA_HOSTING_WINDOW_CLASS", // Windows Terminal
        "mintty",                        // Git Bash, Cygwin, MSYS2
        "VirtualConsoleClass",           // ConEmu, Cmder
        "PuTTY",
        "org.wezfurlong.wezterm",
    ];

    readonly nint _window;

    /// <summary>For a Store app's frame (ApplicationFrameHost), the process of the app inside it, which is the one that copies.</summary>
    readonly uint? _hosted;

    Target(nint window, uint pid, uint? hosted, bool terminal) =>
        (_window, Pid, _hosted, Terminal) = (window, pid, hosted, terminal);

    /// <summary>The process the window belongs to.</summary>
    public uint Pid { get; }

    /// <summary>A terminal, which copies on Ctrl+Insert.</summary>
    internal bool Terminal { get; }

    /// <summary>
    /// The window in front now, or null if there is none; call it on the UI
    /// thread as the shortcut fires, before the user can switch away.
    /// </summary>
    public static Target? InFront()
    {
        var hwnd = WindowFacts.Front();
        if (WindowFacts.ProcessId(hwnd) is not { } pid)
        {
            return null;
        }
        var windowClass = WindowFacts.ClassName(hwnd);
        uint? hosted = windowClass == "ApplicationFrameWindow"
            ? WindowFacts.ProcessId(PInvoke.FindWindowEx(hwnd, HWND.Null, "Windows.UI.Core.CoreWindow", null))
            : null;
        return new Target(Handle(hwnd), pid, hosted, s_terminals.Contains(windowClass));
    }

    /// <summary>Still the window in front.</summary>
    internal bool StillInFront() => Handle(WindowFacts.Front()) == _window;

    /// <summary>
    /// Whether the clipboard's latest write is this app's. An app that wrote
    /// without naming a window counts if the target is still in front, as the
    /// keys went to it a moment ago.
    /// </summary>
    internal unsafe bool WroteClipboard()
    {
        var owner = SystemClipboard.Owner();
        if (owner == 0)
        {
            return StillInFront();
        }
        return owner == _window || WindowFacts.ProcessId((HWND)owner) is { } pid && Owns(pid);
    }

    /// <summary>Whether a process is the target's app: its window's, or the Store app inside its frame.</summary>
    internal bool Owns(uint pid) => pid == Pid || pid == _hosted;

    static unsafe nint Handle(HWND hwnd) => (nint)hwnd.Value;
}
