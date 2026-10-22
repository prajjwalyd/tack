namespace Tack.Core.Phone.Qr;

/// <summary>
/// How a segment of the text is written. Each mode holds every character the
/// one before it holds, so the larger of two modes can write both segments.
/// </summary>
internal enum QrMode
{
    /// <summary>Digits, three to ten bits.</summary>
    Numeric,

    /// <summary>Digits, capitals and " $%*+-./:", two to eleven bits.</summary>
    Alphanumeric,

    /// <summary>Any byte, eight bits each.</summary>
    Byte,
}
