using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Tack.App.Pinboard.Automation;

/// <summary>
/// A canvas that screen readers see as whatever its owner says: the board (a
/// list) and each print's slot (an item of it). A plain canvas has no
/// automation peer and cannot take the keyboard's focus with a peer to speak
/// for it. It draws no focus rectangle of its own; the board draws its ring.
/// </summary>
public sealed partial class PeerCanvas : Canvas
{
    public PeerCanvas() => UseSystemFocusVisuals = false;

    /// <summary>Makes the canvas's peer, once a screen reader first asks; until it is set, the canvas has none.</summary>
    internal Func<AutomationPeer>? Peer { get; set; }

    protected override AutomationPeer? OnCreateAutomationPeer() => Peer?.Invoke();
}
