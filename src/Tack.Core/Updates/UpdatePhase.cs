namespace Tack.Core.Updates;

/// <summary>Where looking for and installing a new version stands.</summary>
public enum UpdatePhase
{
    /// <summary>Nothing has been checked yet.</summary>
    Idle,
    Checking,
    UpToDate,

    /// <summary>A newer version is ready to install.</summary>
    Available,
    Downloading,

    /// <summary>Downloaded and verified; the installer is starting and Tack is about to quit.</summary>
    Installing,
    Error,
}
