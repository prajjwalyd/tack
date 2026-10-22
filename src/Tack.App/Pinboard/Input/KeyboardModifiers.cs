using Microsoft.UI.Input;
using Tack.Core.Pinboard.Input;
using Windows.System;
using Windows.UI.Core;

namespace Tack.App.Pinboard.Input;

/// <summary>The modifier keys held down right now, as the UI thread's keyboard state has them.</summary>
static class KeyboardModifiers
{
    public static KeyModifiers Held()
    {
        var held = KeyModifiers.None;
        if (Down(VirtualKey.Shift))
        {
            held |= KeyModifiers.Shift;
        }
        if (Down(VirtualKey.Control))
        {
            held |= KeyModifiers.Ctrl;
        }
        if (Down(VirtualKey.Menu))
        {
            held |= KeyModifiers.Alt;
        }
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows))
        {
            held |= KeyModifiers.Win;
        }
        return held;
    }

    static bool Down(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
}
