using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace Tack.App.Pinboard.Automation;

/// <summary>
/// A print's Keep or unpin button for screen readers: a button with a name
/// in words (Keep's says whether the print is kept) that does what a click does.
/// </summary>
/// <param name="owner">The button's element.</param>
/// <param name="name">Its name as it is now.</param>
/// <param name="invoke">What pressing it does.</param>
sealed partial class PrintButtonPeer(FrameworkElement owner, Func<string> name, Action invoke)
    : FrameworkElementAutomationPeer(owner), IInvokeProvider
{
    public void Invoke() => invoke();

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string GetClassNameCore() => "TackPrintButton";

    protected override string GetNameCore() => name();

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    /// <summary>Its icon is a picture, not words to read.</summary>
    protected override IList<AutomationPeer>? GetChildrenCore() => PeerList.Fill(base.GetChildrenCore(), []);

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : base.GetPatternCore(patternInterface);
}
