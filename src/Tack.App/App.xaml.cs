using Microsoft.UI.Xaml;
using Tack.Core;
using Tack.Windows.Host;
using Tack.Windows.Shell;

namespace Tack.App;

/// <summary>
/// Tack running in the tray. It opens no window until there is something to
/// show, and keeps running with none open.
/// </summary>
public partial class App : Application
{
    readonly AppIdentity _identity;
    HostWindow? _host;
    TrayIcon? _tray;

    public App(AppIdentity identity)
    {
        _identity = identity;
        InitializeComponent();
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = new HostWindow();
        _tray = new TrayIcon(_host, _identity.DisplayName, Path.Combine(AppContext.BaseDirectory, "Assets", "Tack.ico"));
        _tray.MenuRequested += ShowMenu;
    }

    void ShowMenu()
    {
        if (_host is null)
        {
            return;
        }
        PopupMenu.ShowAtPointer(_host.Handle, [new MenuItem("Quit Tack", Quit)]);
    }

    void Quit()
    {
        _tray?.Dispose();
        _host?.Dispose();
        Exit();
    }
}
