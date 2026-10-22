namespace Tack.Core.Pinboard.Input;

/// <summary>What a drop on the board comes to.</summary>
public enum DropOutcome
{
    /// <summary>Nothing to pin, and nothing to say.</summary>
    Nothing,

    /// <summary>Pictures to pin, each read and pinned.</summary>
    Images,

    /// <summary>A link or text to pin as a note.</summary>
    Text,

    /// <summary>Files, none of them a PNG or JPEG: "Only images and text can be pinned".</summary>
    NotPinnable,

    /// <summary>Pictures, all of them too large: "Can't pin that image".</summary>
    BadImage,
}
