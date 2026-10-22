namespace Tack.Core.Pinboard.Input;

/// <summary>One line of a print's menu: what it says and does, or a separator (no action).</summary>
public sealed record PrintMenuEntry(string Label, PrintMenuAction? Action)
{
    public static PrintMenuEntry Separator { get; } = new(string.Empty, null);

    public bool IsSeparator => Action is null;
}
