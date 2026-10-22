using Tack.Core.Settings;

namespace Tack.App.Settings;

/// <summary>
/// A part of the Settings window: a group of the list (General, Speed,
/// Appearance, Shortcuts, Updates, the phone card) or the phone page. Each
/// builds its controls once and renders by changing them in place, never
/// rebuilding one, so the keyboard focus stays where it is.
/// </summary>
interface ISettingsSection
{
    /// <summary>What a link to it says ("shortcuts"): <see cref="SettingsRoute.Section"/>.</summary>
    string Id { get; }

    /// <summary>Once, when the window opens, before the first <see cref="Render"/>.</summary>
    void Start(SettingsContext context);

    /// <summary>Shows <paramref name="settings"/>: when the window opens and after every change, from here or the tray. UI thread.</summary>
    void Render(TackSettings settings);
}
