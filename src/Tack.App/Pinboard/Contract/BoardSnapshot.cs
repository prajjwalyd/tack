using Tack.Core.Prints;

namespace Tack.App.Pinboard.Contract;

/// <summary>The board as the app has it when the board first asks: the prints in row order, and the look.</summary>
sealed record BoardSnapshot(IReadOnlyList<Print> Prints, BoardLook Look);
