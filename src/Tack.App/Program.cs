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
        var identity = AppIdentity.For(BuildInfo.IsDevelopment);
        using var instance = SingleInstance.TryClaim(identity.MutexName);
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
            _ = new App(identity);
        });
        return 0;
    }
}
