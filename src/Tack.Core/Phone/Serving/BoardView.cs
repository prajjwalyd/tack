namespace Tack.Core.Phone.Serving;

/// <summary>The board as the phone page reads it, without its pictures (the page fetches those by address).</summary>
/// <param name="Pc">This PC's short NetBird name.</param>
/// <param name="You">The asking device's short NetBird name.</param>
/// <param name="Prints">In the board's order: kept first, then the newest.</param>
internal sealed record BoardView(string Pc, string You, IReadOnlyList<PrintView> Prints);
