namespace Tack.Core.Pinboard.Input;

/// <summary>What a drop on the board comes to.</summary>
public enum DropOutcome
{
    /// <summary>Nothing to pin, and nothing to say.</summary>
    Nothing,

    /// <summary>Pictures to read and pin, and files and folders to pin where they are.</summary>
    Files,

    /// <summary>A link or text to pin as a note.</summary>
    Text,

    /// <summary>Files with no place on disk to link to, none of them a picture: "Can't pin files from there".</summary>
    NotPinnable,

    /// <summary>Pictures with no place on disk, all of them too large: "Can't pin that image".</summary>
    BadImage,
}
