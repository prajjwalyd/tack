using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tack.Windows.Shell;

/// <summary>
/// A native popup menu, as the tray icon shows on right-click and a print on
/// the board does, drawn in the person's app mode (<see cref="MenuTheme"/>).
/// Native rather than XAML: it costs nothing while closed. Modal: it returns
/// once the menu has closed, so call it on the owner's thread, outside any
/// input handler of its own.
/// </summary>
/// <remarks>
/// The owner must be a window that can come to the front, such as Tack's
/// hidden host window, never the board (which is never activated): Windows
/// closes a menu on a click elsewhere only while its owner is the foreground
/// window. The window that was in front gets the foreground back once the
/// menu closes, so the keyboard stays where the person left it.
/// </remarks>
public static unsafe class PopupMenu
{
    /// <summary>Shows <paramref name="items"/> at the pointer and runs the one chosen, if any, once the menu has closed.</summary>
    public static void ShowAtPointer(nint owner, IReadOnlyList<MenuItem> items)
    {
        PInvoke.GetCursorPos(out var pointer);
        ShowAt(owner, (pointer.X, pointer.Y), items);
    }

    /// <summary>
    /// Shows <paramref name="items"/> with the menu's corner at <paramref name="at"/>
    /// (physical screen px) and runs the one chosen, if any, once the menu has closed.
    /// </summary>
    public static void ShowAt(nint owner, (int X, int Y) at, IReadOnlyList<MenuItem> items)
    {
        MenuTheme.Match();
        var menu = PInvoke.CreatePopupMenu();
        int picked;
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                Append(menu, items[i], id: (nuint)(i + 1));
            }
            var before = PInvoke.GetForegroundWindow();
            PInvoke.SetForegroundWindow((HWND)owner);
            picked = PInvoke.TrackPopupMenu(
                menu,
                TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY,
                at.X, at.Y, 0, (HWND)owner, null).Value;
            // Lets the menu's loop end cleanly (the documented TrackPopupMenu workaround).
            PInvoke.PostMessage((HWND)owner, PInvoke.WM_NULL, default, default);
            // Back to whoever had the keyboard, unless the person moved on meanwhile; a chosen
            // command that opens a window (Settings) takes the front after this.
            if (!before.IsNull && before != (HWND)owner && PInvoke.GetForegroundWindow() == (HWND)owner)
            {
                PInvoke.SetForegroundWindow(before);
            }
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
