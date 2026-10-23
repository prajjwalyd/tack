using System.Text;

namespace Tack.Core.Tests.Updates.Signing;

/// <summary>
/// The key and signatures of the minisign verifier Tack 0.2 used, from its
/// own tests: each signs the four bytes "test". Wrapped as latest.json and
/// <c>UpdateTrust</c> carry them, as base64 of the .pub and .sig text.
/// </summary>
public static class MinisignSamples
{
    public const string KeyLine = "RWQf6LRCGA9i53mlYecO4IzT51TGPpvWucNSCh1CBM0QTaLn73Y7GFO3";

    public const string PubText = "untrusted comment: minisign public key E7620F1842B4E81F\n" + KeyLine;

    /// <summary>A legacy ("Ed") signature, over the content itself.</summary>
    public const string LegacySigText =
        "untrusted comment: signature from minisign secret key\n" +
        "RWQf6LRCGA9i59SLOFxz6NxvASXDJeRtuZykwQepbDEGt87ig1BNpWaVWuNrm73YiIiJbq71Wi+dP9eKL8OC351vwIasSSbXxwA=\n" +
        "trusted comment: timestamp:1555779966\tfile:test\n" +
        "QtKMXWyYcwdpZAlPF7tE2ENJkRd1ujvKjlj1m9RtHTBnZPa5WKU5uWRs5GoP5M/VqE81QFuMKI5k/SfNQUaOAA==";

    /// <summary>A prehashed ("ED") signature, over the content's BLAKE2b-512 hash.</summary>
    public const string PrehashedSigText =
        "untrusted comment: signature from minisign secret key\n" +
        "RUQf6LRCGA9i559r3g7V1qNyJDApGip8MfqcadIgT9CuhV3EMhHoN1mGTkUidF/z7SrlQgXdy8ofjb7bNJJylDOocrCo8KLzZwo=\n" +
        "trusted comment: timestamp:1556193335\tfile:test\n" +
        "y/rUw2y8/hOUYjZU71eHp/Wo1KZ40fGy2VJEDl34XMJM+TX48Ss/17u3IvIfbVR1FkZZSNCisQbuQY+bHwhEBg==";

    /// <summary>The same prehashed signature with another trusted comment and its own global signature.</summary>
    public const string PrehashedSigText2 =
        "untrusted comment: signature from minisign secret key\n" +
        "RUQf6LRCGA9i559r3g7V1qNyJDApGip8MfqcadIgT9CuhV3EMhHoN1mGTkUidF/z7SrlQgXdy8ofjb7bNJJylDOocrCo8KLzZwo=\n" +
        "trusted comment: timestamp:1633700835\tfile:test\tprehashed\n" +
        "wLMDjy9FLAuxZ3q4NlEvkgtyhrr0gtTu6KC4KBJdITbbOeAi1zBIYo0v4iTgt8jJpIidRJnp94ABQkJAgAooBQ==";

    public static string Pub => Wrap(PubText);

    public static string Legacy => Wrap(LegacySigText);

    public static string Prehashed => Wrap(PrehashedSigText);

    public static string Wrap(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    public static MemoryStream Content(string text = "test") => new(Encoding.UTF8.GetBytes(text));
}
