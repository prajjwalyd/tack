namespace Tack.Core.Updates;

/// <summary>
/// What the updater keeps, and the rules for changing it: when a check may
/// start, what a check or install leaves behind. Not thread-safe; the updater
/// uses it under its own lock.
/// </summary>
public sealed class UpdaterState(UpdateStatus? status = null)
{
    /// <summary>What <see cref="UpdatePhase.Available"/> installs.</summary>
    UpdateOffer? _offer;

    /// <summary>How updates stand.</summary>
    public UpdateStatus Status { get; private set; } = status ?? UpdateStatus.Idle;

    /// <summary>
    /// Starts a check, unless one or an install is going. The automatic one
    /// also needs the setting on and nothing checked yet. A check that starts
    /// drops the version the last one found.
    /// </summary>
    /// <param name="settingOn">"Check for updates when Tack starts", read when the check is due.</param>
    public bool BeginCheck(bool automatic, bool settingOn)
    {
        var allowed = automatic ? settingOn && Status.State == UpdatePhase.Idle : !Status.Busy;
        if (allowed)
        {
            _offer = null;
            Status = Status.Checking();
        }
        return allowed;
    }

    /// <summary>A check ended: <paramref name="offer"/> is ready to install, or nothing newer if null.</summary>
    public void Checked(UpdateOffer? offer, long nowMs)
    {
        _offer = offer;
        Status = offer is null ? UpdateStatus.UpToDate(nowMs) : UpdateStatus.Found(offer.Version, offer.Notes, nowMs);
    }

    /// <summary>
    /// Starts installing what the last check found, and hands it over; null,
    /// with nothing changed, when no version is ready. Once taken, a failure
    /// means it has to be found again.
    /// </summary>
    public UpdateOffer? BeginInstall()
    {
        if (Status.State != UpdatePhase.Available || _offer is not { } offer)
        {
            return null;
        }
        _offer = null;
        Status = Status with { State = UpdatePhase.Downloading, Progress = null };
        return offer;
    }

    /// <summary>The download is <paramref name="percent"/> done. False, with nothing changed, unless downloading.</summary>
    public bool Downloaded(int percent)
    {
        if (Status.State != UpdatePhase.Downloading)
        {
            return false;
        }
        Status = Status with { Progress = Math.Clamp(percent, 0, 100) / 100.0 };
        return true;
    }

    /// <summary>Downloaded and verified: the installer is about to start, and Tack to quit.</summary>
    public void Installing() => Status = Status with { State = UpdatePhase.Installing, Progress = 1.0 };

    /// <summary>A check or install failed: <paramref name="message"/> is shown, the version found and the last good time stay.</summary>
    public void Fail(string message)
    {
        _offer = null;
        Status = Status.Failed(message);
    }
}
