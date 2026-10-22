namespace Tack.Core.Pinboard.Input;

/// <summary>The modifier keys held with a key.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,

    Shift = 1,

    Ctrl = 2,

    Alt = 4,

    /// <summary>Either Windows key.</summary>
    Win = 8,
}
