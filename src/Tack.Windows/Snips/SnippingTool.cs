using Tack.Windows.Foreground;
using Windows.Win32.Foundation;

namespace Tack.Windows.Snips;

/// <summary>Whether a window is Snipping Tool's, or its capture overlay's.</summary>
public static unsafe class SnippingTool
{
    /// <summary>
    /// Whether the window in front belongs to Snipping Tool, such as the
    /// full-screen overlay Win + Shift + S puts up while a region is picked.
    /// Windows can report that overlay as a full-screen app.
    /// </summary>
    public static bool InFront() => Owns(WindowFacts.Front());

    /// <summary>The file name of the app whose window is in front, e.g. "Code.exe"; for development traces.</summary>
    public static string? FrontExe() => WindowFacts.ExeName(WindowFacts.Front());

    /// <summary>Whether a window is Snipping Tool's: by its package, or by its program's full path.</summary>
    internal static bool Owns(nint window) => Owns((HWND)window);

    static bool Owns(HWND hwnd)
    {
        using var process = ProcessQuery.Of(hwnd);
        if (process == null)
        {
            return false;
        }
        return process.Family() == TrustedSnipper.Family
            || (process.Image() is { } image && TrustedSnipper.IsTrusted(image, TrustedSnipper.Folders));
    }
}
