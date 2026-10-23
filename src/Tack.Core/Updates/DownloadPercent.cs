namespace Tack.Core.Updates;

/// <summary>How much of the installer is down, as Settings shows it.</summary>
public static class DownloadPercent
{
    /// <summary>
    /// <paramref name="bytes"/> of <paramref name="total"/> in whole percent,
    /// rounded down and at most 100; null when the size is not known.
    /// </summary>
    public static int? Of(long bytes, long? total)
    {
        if (total is not > 0 || bytes < 0)
        {
            return null;
        }
        return (int)Math.Min((decimal)bytes * 100 / total.Value, 100);
    }
}
