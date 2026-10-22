using System.Globalization;

namespace Tack.Core.Captures;

/// <summary>Local wall-clock time, to name a capture or a note after.</summary>
public readonly record struct Timestamp(int Year, int Month, int Day, int Hour, int Minute, int Second)
{
    public static Timestamp Of(DateTime local) =>
        new(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second);

    /// <summary>"Screenshot YYYY-MM-DD HHMMSS", the name Snipping Tool itself would give the file.</summary>
    public string FileStem() => $"Screenshot {Stamp()}";

    /// <summary>"YYYY-MM-DD HHMMSS".</summary>
    public string Stamp() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:0000}-{Month:00}-{Day:00} {Hour:00}{Minute:00}{Second:00}");
}
