using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Tack.Core.Shortcuts;

/// <summary>
/// A keyboard shortcut as the user sees and saves it, such as "Win+Alt+S"; an
/// empty text turns one off. <paramref name="Vk"/> is the Windows virtual-key code.
/// </summary>
public readonly record struct Chord(bool Win, bool Ctrl, bool Alt, bool Shift, ushort Vk)
{
    /// <summary>Shows or hides the board.</summary>
    public const string DefaultToggle = "Win+Alt+S";

    /// <summary>Pins the current selection.</summary>
    public const string DefaultPin = "Win+Alt+C";

    /// <summary>
    /// Parses "Win+Alt+S": modifiers in any order and case, "Windows", "Control"
    /// and "Ctrl" accepted. An empty text parses as off (<paramref name="chord"/>
    /// null). A chord must hold Win, Ctrl or Alt unless its key is an F key:
    /// Shift and a letter alone would take that capital letter away from every app.
    /// </summary>
    public static bool TryParse(string text, out Chord? chord, [NotNullWhen(false)] out string? error)
    {
        chord = null;
        error = null;
        text = text.Trim();
        if (text.Length == 0)
        {
            return true;
        }
        var (win, ctrl, alt, shift, vk) = (false, false, false, false, (ushort)0);
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "win" or "windows" or "super" or "meta": win = true; break;
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "":
                    error = $"\"{text}\" has an empty part";
                    return false;
                default:
                    if (vk != 0)
                    {
                        error = $"\"{text}\" has more than one key";
                        return false;
                    }
                    if (ChordKeys.Code(part) is not { } code)
                    {
                        error = $"\"{part}\" is not a key Tack can use";
                        return false;
                    }
                    vk = code;
                    break;
            }
        }
        if (vk == 0)
        {
            error = $"\"{text}\" has no key, only modifiers";
            return false;
        }
        if (!(win || ctrl || alt || ChordKeys.IsFunctionKey(vk)))
        {
            error = $"\"{text}\" needs Win, Ctrl or Alt";
            return false;
        }
        chord = new Chord(win, ctrl, alt, shift, vk);
        return true;
    }

    /// <summary>
    /// A shortcut as saved: tidied up ("alt + win + s" becomes "Win+Alt+S"), or
    /// left as it was if it does not parse.
    /// </summary>
    public static string Tidy(string text) =>
        !TryParse(text, out var chord, out _) ? text.Trim()
        : chord is { } parsed ? parsed.ToString()
        : string.Empty;

    /// <summary>"Win+Ctrl+Alt+Shift+K", modifiers always in that order.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var (on, name) in new[] { (Win, "Win"), (Ctrl, "Ctrl"), (Alt, "Alt"), (Shift, "Shift") })
        {
            if (on)
            {
                text.Append(name).Append('+');
            }
        }
        return text.Append(ChordKeys.Name(Vk) ?? $"0x{Vk:X2}").ToString();
    }
}
