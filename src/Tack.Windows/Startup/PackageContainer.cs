using System.ComponentModel;
using System.Diagnostics;
using Tack.Core.Diagnostics;
using Tack.Windows.Shell;

namespace Tack.Windows.Startup;

/// <summary>
/// Escaping an MSIX container. A program started from inside a packaged app
/// (a terminal or coding tool hosted by one) can land in that app's container
/// without a package identity of its own. There Windows redirects AppData
/// writes and namespaces named objects, so Tack would run a stale private
/// copy of the board beside the user's real one. It starts itself again
/// through Explorer instead, and the contained copy exits. Used once at
/// startup, right after <see cref="DllSearch.Secure"/>.
/// </summary>
public static class PackageContainer
{
    /// <summary>
    /// Set before relaunching. Explorer hands the launch to the running shell,
    /// whose environment does not have it, so a relaunched Tack that still
    /// sees it never escaped; it then carries on instead of relaunching forever.
    /// </summary>
    const string Relaunched = "TACK_RELAUNCHED_FROM_PACKAGE";

    /// <summary>Lets a developer keep Tack attached to the console it was started from, accepting the redirected AppData.</summary>
    const string Stay = "TACK_STAY_IN_PACKAGE";

    /// <summary>
    /// How far up the parent chain to look. Hosts sit a few levels up (app,
    /// helper, shell, Tack); the chain ends long before this.
    /// </summary>
    const int MaxAncestors = 16;

    /// <summary>
    /// The package Tack is running under or below, if any: its own identity,
    /// or that of the nearest packaged ancestor process.
    /// </summary>
    public static string? HostPackage() =>
        PackageNames.Own() ?? NearestPackaged(Environment.ProcessId, ProcessParents.Snapshot(), PackageNames.Of);

    /// <summary>
    /// Starts this exe again through Explorer, outside the container. False
    /// when that was already tried, the developer opted out, or it could not
    /// start; Tack then runs where it is. True means this copy should exit.
    /// </summary>
    public static bool RelaunchOutside(string host)
    {
        if (!MayRelaunch(Environment.GetEnvironmentVariable)
            || Environment.ProcessPath is not { } exe
            || Explorer.Exe is not { } explorer)
        {
            return false;
        }
        DevTrace.Write($"started inside {host}'s container; starting again outside it (set {Stay}=1 to stay)");
        Environment.SetEnvironmentVariable(Relaunched, "1");
        try
        {
            using var started = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { exe } });
            return started != null;
        }
        catch (Win32Exception e)
        {
            DevTrace.Write($"cannot start again outside the container: {e.Message}");
            return false;
        }
    }

    /// <summary>Neither relaunched already nor told to stay.</summary>
    internal static bool MayRelaunch(Func<string, string?> environment) =>
        environment(Relaunched) == null && environment(Stay) == null;

    /// <summary>
    /// Walks up from <paramref name="processId"/> to the first ancestor with a
    /// package. A parent id can be reused by an unrelated process that started
    /// later; the walk ends at the root, at a cycle or after a few steps either way.
    /// </summary>
    internal static string? NearestPackaged(int processId, IReadOnlyDictionary<int, int> parents, Func<int, string?> packageOf)
    {
        var current = processId;
        for (var step = 0; step < MaxAncestors; step++)
        {
            if (!parents.TryGetValue(current, out var parent) || parent == 0 || parent == current)
            {
                return null;
            }
            if (packageOf(parent) is { } package)
            {
                return package;
            }
            current = parent;
        }
        return null;
    }
}
