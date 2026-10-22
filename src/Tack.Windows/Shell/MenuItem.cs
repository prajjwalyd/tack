namespace Tack.Windows.Shell;

/// <summary>One line of a <see cref="PopupMenu"/>: a command, or a separator.</summary>
/// <param name="Text">What the line says; empty for a separator.</param>
/// <param name="Run">What choosing it does.</param>
/// <param name="Checked">Shows a tick, for settings that are on.</param>
/// <param name="Enabled">Greyed out and not choosable when false.</param>
public sealed record MenuItem(string Text, Action? Run = null, bool Checked = false, bool Enabled = true)
{
    public static MenuItem Separator { get; } = new(string.Empty);

    public bool IsSeparator => Text.Length == 0;
}
