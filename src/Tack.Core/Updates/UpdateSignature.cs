using Tack.Core.Updates.Signing;

namespace Tack.Core.Updates;

/// <summary>
/// Whether a downloaded installer may run: Tack's key signed it, and signed
/// it for the version the feed announced. The feed itself is not signed, so
/// without the second rule an old signed installer could be passed off as a
/// new release.
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// Checks <paramref name="installer"/> (read to its end) against
    /// <paramref name="signature"/> and <paramref name="announced"/>.
    /// </summary>
    /// <exception cref="UpdateFailureException">A signature failure: not Tack's key, a changed file, no version signed, or another version.</exception>
    public static void Check(string signature, Stream installer, string announced)
    {
        var comment = Minisign.Verify(UpdateTrust.PublicKey, signature, installer)
            ?? throw new UpdateFailureException(UpdateFailure.Signature, "the signature does not verify");
        var signed = Minisign.SignedVersion(comment)
            ?? throw new UpdateFailureException(UpdateFailure.Signature, "the signature names no version");
        if (!SameVersion(signed, announced))
        {
            throw new UpdateFailureException(UpdateFailure.Signature, $"signed for {signed}, announced as {announced}");
        }
    }

    /// <summary>
    /// The version signed is the one announced: compared as versions, so "v0.3.1"
    /// and "0.3.1" match, or as text when either is not a version.
    /// </summary>
    public static bool SameVersion(string signed, string announced) =>
        SemVersion.TryParse(signed, out var a) && SemVersion.TryParse(announced, out var b)
            ? a == b
            : string.Equals(signed, announced, StringComparison.Ordinal);
}
