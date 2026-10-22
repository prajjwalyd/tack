namespace Tack.Core.Phone;

/// <summary>A device waiting for the user's answer.</summary>
/// <param name="Code">Shown on the PC and on the device's own page, to compare.</param>
/// <param name="SinceMs">When it asked, ms since the Unix epoch.</param>
public sealed record Asking(string Key, string Name, string Fqdn, string Ip, string Code, long SinceMs);
