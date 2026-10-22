using Microsoft.UI.Xaml.Automation.Peers;

namespace Tack.App.Pinboard.Automation;

/// <summary>
/// A peer's children as the framework will take them. It drops a list made
/// here without a word and keeps only its own collection, the one the default
/// children come in, so that collection is emptied and filled instead.
/// </summary>
static class PeerList
{
    /// <param name="own">The framework's collection, from the base peer's children; null when the element has none.</param>
    /// <param name="peers">The children wanted, in order.</param>
    /// <returns>The framework's collection holding <paramref name="peers"/>, or null (no children) if it gave none and none are wanted.</returns>
    public static IList<AutomationPeer>? Fill(IList<AutomationPeer>? own, IEnumerable<AutomationPeer> peers)
    {
        var wanted = peers.ToList();
        if (own is null)
        {
            return wanted.Count == 0 ? null : wanted;
        }
        own.Clear();
        foreach (var peer in wanted)
        {
            own.Add(peer);
        }
        return own;
    }
}
