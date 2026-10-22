using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Tack.Core;
using Tack.Core.Diagnostics;
using Tack.Core.Storage;
using Tack.Windows.Startup;

namespace Tack.App;

/// <summary>
/// Tack's entry point. DLL loading is locked down and another running Tack is
/// checked for before any UI starts, so a second launch costs next to nothing.
/// </summary>
static class Program
{
    [STAThread]
    static int Main()
    {
        DllSearch.Secure();
        // Started inside another app's MSIX container: run as a normal app instead.
        if (PackageContainer.HostPackage() is { } host && PackageContainer.RelaunchOutside(host))
        {
            return 0;
        }
        var identity = AppIdentity.For(BuildInfo.IsDevelopment);
        using var instance = SingleInstance.TryClaim(identity.MutexName + DevInstance());
        if (instance is null)
        {
            DevTrace.Write("another Tack is running; this one leaves");
            return 0;
        }
        var paths = TackPaths.Resolve(Environment.GetEnvironmentVariable, BuildInfo.IsDevelopment);
        DevTrace.Write($"data in {paths.Roaming} and {paths.Local}");

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(started =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(identity, paths);
        });
        return 0;
    }

    /// <summary>
    /// Development builds: TACK_DEV_INSTANCE gives a build a single-instance
    /// name of its own, so several can run side by side (each with its own
    /// TACK_HOME), as the board-picture script does.
    /// </summary>
    static string DevInstance()
    {
#if DEBUG
        return Environment.GetEnvironmentVariable("TACK_DEV_INSTANCE") is { Length: > 0 } name ? "." + name : "";
#else
        return "";
#endif
    }
}
