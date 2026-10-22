using Tack.Core.Appearance;

namespace Tack.Core.Pinboard.Look;

/// <summary>
/// The frame finishes the Frame setting picks from, defined here and nowhere
/// else (the settings only know the ids), and where each tone sits across a rail.
/// </summary>
public static class FrameFinishes
{
    /// <summary>Every finish, in <see cref="Frame.Presets"/> order; the first is the default.</summary>
    public static IReadOnlyList<FrameFinish> All { get; } =
    [
        // Brushed aluminium, cool grey.
        new("aluminium",
            FinishTones.Parse(
                ["#ffffff", "#e4e7eb", "#d2d6db", "#bcc1c7", "#a6acb3", "#7b818a"],
                ["#7f858e", "#aab0b7", "#c3c8ce", "#d8dce1", "#ffffff"],
                ["#eef0f3", "#b3b9c0", "#ffffff", "#767c85"]),
            FinishTones.Parse(
                ["#e8ebee", "#b9bec4", "#a8aeb5", "#92989f", "#7c828a", "#50555c"],
                ["#565b62", "#80868d", "#969ca3", "#aab0b6", "#e0e3e6"],
                ["#c6cbd0", "#858b93", "#eef0f2", "#4d5259"])),
        // Dark anodised steel.
        new("graphite",
            FinishTones.Parse(
                ["#777f89", "#696f79", "#5e646d", "#52575e", "#45494f", "#2b2e31"],
                ["#2d3034", "#474c52", "#565b63", "#626871", "#777f89"],
                ["#6e757f", "#4d5158", "#777f89", "#282a2e"]),
            FinishTones.Parse(
                ["#7a808a", "#5b6068", "#535860", "#4a4e55", "#3d4147", "#26282b"],
                ["#2a2c30", "#40444a", "#4b5058", "#585d65", "#7a808a"],
                ["#5f656d", "#40444a", "#7a808a", "#202225"])),
        // Brushed brass: a quieter, greener gold than the kept pin's polished one.
        new("brass",
            FinishTones.Parse(
                ["#fff3cf", "#ecdca6", "#dcc78a", "#c9ae66", "#b3944a", "#7e6528"],
                ["#836a2c", "#b9a05a", "#cfb978", "#e3d3a0", "#fff3cf"],
                ["#f0e2b4", "#c3a560", "#fff3cf", "#8a6f30"]),
            FinishTones.Parse(
                ["#eadfbf", "#caba8d", "#c3af77", "#b89f5a", "#a58c47", "#6c5c2e"],
                ["#746232", "#a99049", "#bba25f", "#c4b079", "#dbd3bd"],
                ["#cfc29e", "#b0964c", "#e3ddcd", "#69592d"])),
        // Polished steel: lighter and whiter than the aluminium, with no blue in it.
        new("silver",
            FinishTones.Parse(
                ["#ffffff", "#f2f3f4", "#e6e7e9", "#d6d8da", "#c2c4c7", "#9a9da1"],
                ["#9da0a4", "#c7c9cb", "#dcdddf", "#eceded", "#ffffff"],
                ["#f8f8f9", "#cfd0d2", "#ffffff", "#9a9da1"]),
            FinishTones.Parse(
                ["#eceded", "#d0d2d4", "#bfc1c4", "#a8aaae", "#94979b", "#66696d"],
                ["#6e7175", "#999ca0", "#adb0b3", "#c0c2c5", "#e8e9ea"],
                ["#d8d9db", "#9fa1a5", "#f2f2f3", "#626569"])),
    ];

    /// <summary>
    /// The bottom rail's tones from its top down, over the frame's 6 px: the
    /// hairline for half a pixel, the bevel, the face from just past the
    /// middle, and the shaded edge in the last pixel.
    /// </summary>
    public static IReadOnlyList<RailStop> BottomStops { get; } =
    [
        new(0, 0), new(0.5, 0), new(0.5, 1), new(1.5, 2), new(0.55 * BoardMetrics.FrameWidth, 3),
        new(BoardMetrics.FrameWidth - 1, 4), new(BoardMetrics.FrameWidth - 0.5, 5),
    ];

    /// <summary>A side rail's tones from its outer edge in: the shaded edge, the bevel, the face, the lit inner edge.</summary>
    public static IReadOnlyList<RailStop> SideStops { get; } =
    [
        new(0, 0), new(0.5, 0), new(1, 1), new(0.45 * BoardMetrics.FrameWidth, 2),
        new(BoardMetrics.FrameWidth - 1, 3), new(BoardMetrics.FrameWidth - 0.5, 4),
    ];

    public static FrameFinish Default => All[0];

    /// <summary>The finish of <paramref name="frame"/>; the default for one this list does not have.</summary>
    public static FrameFinish For(Frame frame) => All.FirstOrDefault(f => f.Id == frame.Id) ?? Default;
}
