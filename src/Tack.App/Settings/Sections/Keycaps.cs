using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tack.App.Settings.Sections;

/// <summary>What a shortcut field shows: a keycap for each key, "Off", or what it waits for while it listens.</summary>
static class Keycaps
{
    /// <summary>Fills <paramref name="field"/>: <paramref name="keys"/> as keycaps, after "Press a shortcut…" or with "…" while it listens.</summary>
    public static void Fill(ContentControl field, IReadOnlyList<string> keys, bool listening)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var key in keys)
        {
            row.Children.Add(new Border
            {
                Style = Look("SettingsKeycap"),
                Child = new TextBlock { Text = key, Style = Look("SettingsKeycapText") },
            });
        }
        if (listening)
        {
            row.Children.Add(new TextBlock { Text = keys.Count > 0 ? "…" : "Press a shortcut…", Style = Look("SettingsFieldNote") });
        }
        else if (keys.Count == 0)
        {
            row.Children.Add(new TextBlock { Text = "Off", Style = Look("SettingsFieldOff") });
        }
        field.Content = row;
    }

    /// <summary>One of the Settings window's shared styles (SettingsStyles.xaml).</summary>
    static Style Look(string key) => XamlStyles.App(key);
}
