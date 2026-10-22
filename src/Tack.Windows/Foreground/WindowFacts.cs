using Windows.Win32;
using Windows.Win32.Foundation;

namespace Tack.Windows.Foreground;

/// <summary>What Windows says about a window: its process, its class, its program. Never its title.</summary>
static unsafe class WindowFacts
{
    /// <summary>The window in front.</summary>
    public static HWND Front() => PInvoke.GetForegroundWindow();

    /// <summary>The process a window belongs to, or null for no window (or one that is gone).</summary>
    public static uint? ProcessId(HWND hwnd)
    {
        if (hwnd.IsNull)
        {
            return null;
        }
        uint pid = 0;
        PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        return pid != 0 ? pid : null;
    }

    /// <summary>A window's class name, "" if it has none (or is gone).</summary>
    public static string ClassName(HWND hwnd)
    {
        if (hwnd.IsNull)
        {
            return "";
        }
        Span<char> name = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, name);
        return length > 0 ? name[..length].ToString() : "";
    }

    /// <summary>The file name of the program that owns a window, e.g. "SnippingTool.exe".</summary>
    public static string? ExeName(HWND hwnd)
    {
        using var process = ProcessQuery.Of(hwnd);
        return process?.Image() is { } image ? Path.GetFileName(image) : null;
    }
}
