namespace Tack.Core.Pinboard.Input;

/// <summary>What a line of a print's menu does.</summary>
public enum PrintMenuAction
{
    /// <summary>The picture, or a note's text (a link's URL), onto the clipboard.</summary>
    Copy,

    /// <summary>The default app, or the browser for a link.</summary>
    Open,

    /// <summary>The shell's "edit": Paint for a picture.</summary>
    Edit,

    /// <summary>Explorer on the file's folder, the file selected.</summary>
    ShowInExplorer,

    /// <summary>A capture moves to the Screenshots folder and is no longer Tack's to clean up.</summary>
    SaveToPictures,

    Keep,

    StopKeeping,

    /// <summary>Unpinned with a fall; a capture's or note's own file goes to the Recycle Bin.</summary>
    Unpin,

    /// <summary>Unpinned, and the file, whoever's it is, goes to the Recycle Bin.</summary>
    Recycle,
}
