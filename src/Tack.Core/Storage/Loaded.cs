namespace Tack.Core.Storage;

/// <summary>
/// How reading board.json went. Only <see cref="Ok"/> means Tack knows for
/// certain which captures the board still uses, so only then may leftover
/// captures be swept.
/// </summary>
public abstract record Loaded
{
    Loaded()
    {
    }

    /// <summary>Whether saving would overwrite a board.json that was never backed up.</summary>
    public bool MustNotSave => this is Unreadable { Backup: null };

    /// <summary>Read and understood.</summary>
    public sealed record Ok : Loaded
    {
        public static Ok Instance { get; } = new();
    }

    /// <summary>
    /// There is no board.json. Captures lying around may belong to one that
    /// was deleted, so they are left alone.
    /// </summary>
    public sealed record Missing : Loaded
    {
        public static Missing Instance { get; } = new();
    }

    /// <summary>
    /// board.json exists but could not be read, did not parse, comes from a
    /// newer Tack, or lists no prints at all. <paramref name="Backup"/> is where a
    /// copy of it was put, if one could be made; without one, nothing may write over it.
    /// </summary>
    public sealed record Unreadable(string Reason, string? Backup) : Loaded;
}
