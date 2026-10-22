namespace Tack.App.Pinboard.Prints;

/// <summary>
/// A baked shadow stack, drawn as a nine grid around a box of any size. Its
/// picture holds the shadow of a small box with nothing drawn inside the box,
/// so a faded print never shows its shadow through itself.
/// </summary>
/// <param name="Name">The picture, before its theme and scale.</param>
/// <param name="Margin">From the picture's edge to the box, logical px: as far as the shadow reaches.</param>
/// <param name="Inset">From the picture's edge to where the shadow runs straight along the box, logical px: the grid's lines.</param>
sealed record ShadowArt(string Name, double Margin, double Inset)
{
    /// <summary>A print at rest: a tight contact shadow and a softer one lower down.</summary>
    public static ShadowArt Rest { get; } = new("rest", 24, 44);

    /// <summary>A note at rest: the print's shadow and its free corner standing a hair proud.</summary>
    public static ShadowArt Note { get; } = new("note", 24, 48);

    /// <summary>The hover lift, faded in over the rest shadow.</summary>
    public static ShadowArt Lift { get; } = new("lift", 40, 84);
}
