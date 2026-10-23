using System.Diagnostics;

namespace Tack.Windows.Updates;

/// <summary>
/// Starts a verified installer and leaves it running on its own: it waits
/// for Tack to quit, installs, and starts Tack again.
/// </summary>
public static class InstallerLauncher
{
    /// <summary>
    /// Passive (progress only), an update (keep everything of the person's),
    /// and restart Tack when done: exactly what Tack 0.2's updater passed, so
    /// one installer serves both.
    /// </summary>
    public const string Arguments = "/P /UPDATE /R";

    /// <summary>Starts <paramref name="installer"/> through the shell, detached from Tack.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">It could not be started.</exception>
    public static void Start(string installer)
    {
        var start = new ProcessStartInfo(installer, Arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installer) ?? string.Empty,
        };
        using var started = Process.Start(start);
    }
}
