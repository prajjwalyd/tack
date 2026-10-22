using System.Globalization;
using Tack.Core.Pinboard.Words;

namespace Tack.Core.Updates;

/// <summary>
/// The words of the Updates row, by phase: a headline, a line under it, the
/// one button that fits, and the progress bar. Pure, so the wording is easy
/// to check.
/// </summary>
public static class UpdateWords
{
    /// <summary>Said when a check the person asked for could not run.</summary>
    public const string CheckFailed = "Couldn't check for updates. Try again.";

    /// <summary>Said when an install the person asked for could not start.</summary>
    public const string InstallFailed = "Couldn't install the update. Try again.";

    static readonly UpdateButton s_check = new("Check for updates", UpdateAction.Check);

    /// <summary>What to show for <paramref name="status"/> at <paramref name="nowMs"/> (for "Last checked 5 minutes ago").</summary>
    /// <param name="culture">For day and month names; the current culture if null.</param>
    /// <param name="zone">Where midnight is; the local zone if null.</param>
    public static UpdateView Describe(UpdateStatus status, long nowMs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        var checkedLine = status.CheckedAt is { } then
            ? "Last checked " + Age.Ago(then, nowMs, on: "on ", culture: culture, zone: zone)
            : string.Empty;
        var version = status.Version ?? string.Empty;
        switch (status.State)
        {
            case UpdatePhase.Checking:
                return new("Checking for updates…", string.Empty, new("Checking…", UpdateAction.None, Busy: true));
            case UpdatePhase.UpToDate:
                return new("Tack is up to date", checkedLine, s_check);
            case UpdatePhase.Available:
                return new($"Version {version} is available", "Tack restarts to finish installing it.",
                    new("Install", UpdateAction.Install, Accent: true));
            case UpdatePhase.Downloading:
                var known = status.Progress is { } progress ? Math.Clamp(progress, 0, 1) : (double?)null;
                return new($"Downloading version {version}…", known is { } done ? $"{Percent(done)}%" : string.Empty,
                    new("Downloading…", UpdateAction.None, Busy: true), new UpdateMeter(known));
            case UpdatePhase.Installing:
                return new($"Installing version {version}…", "Tack restarts when it is done.",
                    new("Installing…", UpdateAction.None, Busy: true), new UpdateMeter(null));
            case UpdatePhase.Error:
                return new("Couldn't check for updates",
                    string.IsNullOrEmpty(status.Error) ? "Try again in a little while." : status.Error,
                    new("Try again", UpdateAction.Check));
            default:
                return new("Check for updates", checkedLine, s_check);
        }
    }

    /// <summary>What a screen reader is told when the phase changes: the headline, and for an error its line too.</summary>
    public static string Announcement(UpdateStatus status, UpdateView view) =>
        status.State == UpdatePhase.Error ? $"{view.Title}. {view.Sub}" : view.Title;

    /// <summary>A fraction as a whole percentage, halves rounded up.</summary>
    public static int Percent(double fraction) => (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
}
