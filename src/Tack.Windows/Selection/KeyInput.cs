using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Tack.Windows.Selection;

/// <summary>Key presses sent to the app in front, as if typed.</summary>
static unsafe class KeyInput
{
    public static INPUT Key(VIRTUAL_KEY key, bool up) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new()
        {
            ki = new KEYBDINPUT { wVk = key, dwFlags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0 },
        },
    };

    /// <summary>An extended key (Insert, the arrows...), with its scan code, which some terminals read instead of the key code.</summary>
    public static INPUT Extended(VIRTUAL_KEY key, bool up)
    {
        var input = Key(key, up);
        input.Anonymous.ki.wScan = (ushort)PInvoke.MapVirtualKey((uint)key, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);
        input.Anonymous.ki.dwFlags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        return input;
    }

    /// <summary>Sends the key events; false if Windows refused some (another desktop, or an app with higher rights in front).</summary>
    public static bool Send(ReadOnlySpan<INPUT> inputs)
    {
        fixed (INPUT* first = inputs)
        {
            return PInvoke.SendInput((uint)inputs.Length, first, sizeof(INPUT)) == inputs.Length;
        }
    }
}
