using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Shell;

/// <summary>
/// A native popup menu, as the tray icon shows on right-click and a print on
/// the board does. Native rather than XAML: it costs nothing while closed.
/// Modal: it returns once the menu has closed, so call it on the owner's
/// thread, outside any input handler of its own.
/// </summary>
public static unsafe class PopupMenu
{
    /// <summary>Shows <paramref name="items"/> at the pointer and runs the one chosen, if any, once the menu has closed.</summary>
    /// <param name="takeForeground">
    /// Brings <paramref name="owner"/> to the front first, which a hidden owner
    /// (the tray's) needs for the menu to close on a click elsewhere.
    /// </param>
    public static void ShowAtPointer(nint owner, IReadOnlyList<MenuItem> items, bool takeForeground = true)
    {
        PInvoke.GetCursorPos(out var pointer);
        ShowAt(owner, (pointer.X, pointer.Y), items, takeForeground);
    }

    /// <summary>
    /// Shows <paramref name="items"/> with the menu's corner at <paramref name="at"/>
    /// (physical screen px) and runs the one chosen, if any, once the menu has closed.
    /// </summary>
    public static void ShowAt(nint owner, (int X, int Y) at, IReadOnlyList<MenuItem> items, bool takeForeground = true)
    {
        var menu = PInvoke.CreatePopupMenu();
        int picked;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                Append(menu, items[i], id: (nuint)(i + 1));
            }
            if (takeForeground)
            {
                // Without this a hidden owner's menu would not close when the user clicks elsewhere.
                PInvoke.SetForegroundWindow((HWND)owner);
            }
            picked = PInvoke.TrackPopupMenu(
                menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
                at.X, at.Y, 0, (HWND)owner, null).Value;
            PInvoke.PostMessage((HWND)owner, PInvoke.WM_NULL, default, default);
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
        if (picked > 0)
        {
            items[picked - 1].Run?.Invoke();
        }
    }

    static void Append(HMENU menu, MenuItem item, nuint id)
    {
        if (item.IsSeparator)
        {
            PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
            return;
        }
        var flags = MENU_ITEM_FLAGS.MF_STRING
            | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : default)
            | (item.Enabled ? default : MENU_ITEM_FLAGS.MF_GRAYED);
        fixed (char* text = item.Text)
        {
            PInvoke.AppendMenu(menu, flags, id, (PCWSTR)text);
        }
    }
}
