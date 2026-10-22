namespace Tack.Core.Updates;

/// <summary>
/// The updater, as Settings uses it: how updates stand, word when that
/// changes, and the two things the person can ask for. Settings shows
/// <see cref="Status"/> whole after every <see cref="StatusChanged"/>.
/// </summary>
public interface IUpdater
{
    /// <summary>How updates stand now.</summary>
    UpdateStatus Status { get; }

    /// <summary><see cref="Status"/> changed. Raised on the UI thread.</summary>
    event Action? StatusChanged;

    /// <summary>Looks for a new version now. The status it ends in, or null if the check could not run at all.</summary>
    Task<UpdateStatus?> CheckAsync();

    /// <summary>Downloads and installs the version found; Tack restarts. The status it ends in, or null if it could not start.</summary>
    Task<UpdateStatus?> InstallAsync();
}
