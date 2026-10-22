namespace Tack.Core.Updates;

/// <summary>
/// What Settings is told about updates, whole, on every change: the phase and
/// what goes with it. The release notes are another party's text, so they are
/// only ever shown as text, and kept short (<see cref="CleanNotes"/>).
/// </summary>
public sealed record UpdateStatus
{
    /// <summary>The most characters of release notes kept.</summary>
    public const int NotesMaxChars = 2000;

    public UpdatePhase State { get; init; }

    /// <summary>The new version, as "0.3.1", once one is known.</summary>
    public string? Version { get; init; }

    /// <summary>The release notes: untrusted text, at most <see cref="NotesMaxChars"/> long.</summary>
    public string? Notes { get; init; }

    /// <summary>How much of the download is done, 0 to 1; null if it is not downloading or the size is not known.</summary>
    public double? Progress { get; init; }

    /// <summary>A short message for the person, never a raw error.</summary>
    public string? Error { get; init; }

    /// <summary>When the last successful check finished, ms since the Unix epoch.</summary>
    public long? CheckedAt { get; init; }

    /// <summary>Nothing checked yet.</summary>
    public static UpdateStatus Idle { get; } = new();

    /// <summary>Work is going on, so another check or install must not start.</summary>
    public bool Busy => State is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;

    /// <summary>A check began: all but the last good time is dropped.</summary>
    public UpdateStatus Checking() => new() { State = UpdatePhase.Checking, CheckedAt = CheckedAt };

    /// <summary>A check found <paramref name="version"/>.</summary>
    public static UpdateStatus Found(string version, string? notes, long nowMs) =>
        new() { State = UpdatePhase.Available, Version = version, Notes = CleanNotes(notes), CheckedAt = nowMs };

    /// <summary>A check found nothing newer.</summary>
    public static UpdateStatus UpToDate(long nowMs) => new() { State = UpdatePhase.UpToDate, CheckedAt = nowMs };

    /// <summary>A check or an install failed: <paramref name="message"/> is shown, the version and the last good time stay.</summary>
    public UpdateStatus Failed(string message) =>
        this with { State = UpdatePhase.Error, Notes = null, Progress = null, Error = message };

    /// <summary>
    /// The notes as Settings gets them: nothing if blank, otherwise cut to
    /// <see cref="NotesMaxChars"/> characters, with an ellipsis if longer. Only
    /// the length is touched: the text is never treated as markup.
    /// </summary>
    public static string? CleanNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }
        // Counted in Unicode characters, so a pair of surrogates is one and is never cut in half.
        var count = 0;
        var cut = 0;
        foreach (var rune in notes.EnumerateRunes())
        {
            if (count == NotesMaxChars)
            {
                return notes[..cut] + "…";
            }
            count++;
            cut += rune.Utf16SequenceLength;
        }
        return notes;
    }
}
