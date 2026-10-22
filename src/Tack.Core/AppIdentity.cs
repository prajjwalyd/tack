namespace Tack.Core;

/// <summary>
/// The names Windows knows Tack by. The release names must stay exactly as
/// Tack 0.2 used them, or an upgraded PC would run the old and the new app at
/// once and start both with Windows. A development build has names of its own,
/// so it runs beside the installed Tack and never touches its autostart.
/// </summary>
/// <param name="MutexName">The single-instance mutex.</param>
/// <param name="RunValueName">The value under HKCU\...\CurrentVersion\Run that starts Tack with Windows.</param>
/// <param name="DisplayName">What the tray icon's tooltip says.</param>
public sealed record AppIdentity(string MutexName, string RunValueName, string DisplayName)
{
    public static AppIdentity Release { get; } = new(@"Local\app.tack.board", "Tack", "Tack");

    public static AppIdentity Development { get; } =
        new(@"Local\app.tack.board.dev", "Tack (development)", "Tack (development)");

    public static AppIdentity For(bool development) => development ? Development : Release;
}
