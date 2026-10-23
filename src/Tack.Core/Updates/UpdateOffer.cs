namespace Tack.Core.Updates;

/// <summary>A newer version a check found, and what installing it takes.</summary>
/// <param name="Version">The version, as "0.3.1": the announced one, which the signature must name.</param>
/// <param name="Notes">The release notes, untrusted text; null for none.</param>
/// <param name="Url">Where this PC's installer is; always https.</param>
/// <param name="Signature">The installer's minisign signature, the base64 of the .sig text.</param>
public sealed record UpdateOffer(string Version, string? Notes, Uri Url, string Signature);
