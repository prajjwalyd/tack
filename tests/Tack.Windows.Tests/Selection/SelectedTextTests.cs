using Tack.Windows.Clipboard;
using Tack.Windows.Selection;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace Tack.Windows.Tests.Selection;

// UI Automation reads an edit box of the test's own; nothing touches the clipboard.
public sealed unsafe class SelectedTextTests
{
    static ClipboardContent? Selection(EditBox box)
    {
        var automation = SelectedText.Automation();
        IUIAutomationElement* element = null;
        try
        {
            element = automation->ElementFromHandle((HWND)box.Handle);
            return SelectedText.Of(element);
        }
        finally
        {
            if (element != null)
            {
                element->Release();
            }
            automation->Release();
        }
    }

    [Fact]
    public void Selected_text_is_read_without_copying()
    {
        using var box = new EditBox("pin this part only", 4, 13);
        Assert.Equal(new ClipboardContent.TextContent("this part"), Selection(box));
    }

    [Fact]
    public void No_selection_leaves_it_to_the_app_to_copy()
    {
        using var box = new EditBox("nothing selected", 3, 3);
        Assert.Null(Selection(box));
    }

    [Fact]
    public void A_password_box_is_private()
    {
        using var box = new EditBox("hunter2", 0, 7, password: true);
        Assert.Same(ClipboardContent.Private, Selection(box));
    }
}
