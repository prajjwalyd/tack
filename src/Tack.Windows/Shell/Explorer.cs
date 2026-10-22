using System.ComponentModel;
using System.Diagnostics;
using Tack.Core.Diagnostics;

namespace Tack.Windows.Shell;

/// <summary>Windows Explorer, always started by its full path. Safe to call from any thread.</summary>
public static class Explorer
{
    /// <summary>
    /// <c>%WINDIR%\explorer.exe</c>, by its full path from Windows itself: a
    /// bare name would be looked for in the current folder and on PATH first.
    /// Null if Windows will not say where its folder is.
    /// </summary>
    public static string? Exe
    {
        get
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return windows.Length == 0 ? null : Path.Combine(windows, "explorer.exe");
        }
    }

    /// <summary>Opens Explorer on the file's folder, with the file selected.</summary>
    public static void ShowInFolder(string path)
    {
        if (Exe is not { } exe)
        {
            DevTrace.Write("explorer: cannot find the Windows folder to start Explorer");
            return;
        }
        try
        {
            using var started = Process.Start(new ProcessStartInfo(exe, SelectArguments(path)));
        }
        catch (Win32Exception e)
        {
            DevTrace.Write($"explorer: cannot start: {e.Message}");
        }
    }

    /// <summary>Explorer parses its own command line, so the quotes are passed raw.</summary>
    internal static string SelectArguments(string path) => $"/select,\"{path}\"";
}
