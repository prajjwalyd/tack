using Tack.Core.Diagnostics;
using Tack.Windows.Clipboard;
using Windows.Win32;
using Windows.Win32.UI.Accessibility;

namespace Tack.Windows.Selection;

/// <summary>
/// The text selected in the window in front, read through UI Automation the
/// way a screen reader reads it: nothing is copied, so the user's clipboard
/// and clipboard history are never touched. Browsers, Office, Notepad,
/// terminals and most editors answer. Never on the UI thread: UI Automation
/// must not call into the thread that asks it.
/// </summary>
static unsafe class SelectedText
{
    /// <summary>How long the app gets to answer (ms), so a hung one costs a moment rather than Windows' 20 seconds.</summary>
    const uint Patience = 1000;

    /// <summary>
    /// The selection in <paramref name="target"/>: its text, or
    /// <see cref="ClipboardContent.Private"/> in a password box; null when the
    /// app does not say or nothing is selected, so the app is asked to copy
    /// instead (a picture, files, or an app UI Automation cannot read).
    /// </summary>
    public static ClipboardContent? Read(Target target)
    {
        IUIAutomation2* automation = null;
        IUIAutomationElement* focused = null;
        try
        {
            automation = Automation();
            focused = automation->GetFocusedElement();
            return focused != null && target.Owns((uint)focused->CurrentProcessId) ? Of(focused) : null;
        }
        catch (Exception e)
        {
            // The app went away, would not answer, or has nothing focused: it is asked to copy instead.
            DevTrace.Write($"pin selection: UI Automation did not say ({e.Message})");
            return null;
        }
        finally
        {
            if (focused != null)
            {
                focused->Release();
            }
            if (automation != null)
            {
                automation->Release();
            }
        }
    }

    /// <summary>UI Automation, with the patience above; the caller releases it.</summary>
    internal static IUIAutomation2* Automation()
    {
        CUIAutomation8.CreateInstance(out IUIAutomation2* automation).ThrowOnFailure();
        automation->ConnectionTimeout = Patience;
        automation->TransactionTimeout = Patience;
        return automation;
    }

    /// <summary>The selection in one element, as <see cref="Read"/> says it. Throws when the app does not answer.</summary>
    internal static ClipboardContent? Of(IUIAutomationElement* element)
    {
        if (element->CurrentIsPassword)
        {
            return ClipboardContent.Private;
        }
        var iid = IUIAutomationTextPattern.IID_Guid;
        var pattern = (IUIAutomationTextPattern*)element->GetCurrentPatternAs(UIA_PATTERN_ID.UIA_TextPatternId, &iid);
        if (pattern == null)
        {
            return null;
        }
        IUIAutomationTextRangeArray* ranges = null;
        try
        {
            ranges = pattern->GetSelection();
            var parts = new string[ranges->Length];
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = Text(ranges->GetElement(i));
            }
            var text = string.Join("\n", parts);
            return ClipboardView.HasContent(text) ? new ClipboardContent.TextContent(text) : null;
        }
        finally
        {
            if (ranges != null)
            {
                ranges->Release();
            }
            pattern->Release();
        }
    }

    /// <summary>One selected range's text; the range is let go.</summary>
    static string Text(IUIAutomationTextRange* range)
    {
        try
        {
            var text = range->GetText(-1);
            try
            {
                return text.ToString() ?? "";
            }
            finally
            {
                PInvoke.SysFreeString(text);
            }
        }
        finally
        {
            range->Release();
        }
    }
}
