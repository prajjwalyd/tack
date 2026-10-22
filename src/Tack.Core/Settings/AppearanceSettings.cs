using Tack.Core.Appearance;

namespace Tack.Core.Settings;

/// <summary>How the board looks. Each choice falls back to its default on its own.</summary>
public sealed record AppearanceSettings
{
    /// <summary>The board's colour.</summary>
    public Felt Felt { get; init; } = Felt.Default;

    /// <summary>The finish of the metal frame around it.</summary>
    public Frame Frame { get; init; } = Frame.Default;

    /// <summary>The colour of the paper notes.</summary>
    public NotePaper Paper { get; init; } = NotePaper.Default;
}
