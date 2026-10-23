namespace Tack.Core.Updates.Signing;

/// <summary>
/// A minisign signature, read from the text of its .sig file, four lines:
/// <list type="number">
/// <item>an untrusted comment, which nothing signs and nothing reads;</item>
/// <item>the base64 of 74 bytes: the algorithm, the signer's 8-byte key id and
/// the 64-byte Ed25519 signature;</item>
/// <item>"trusted comment: " and the comment;</item>
/// <item>the base64 of the 64-byte global signature, over the signature and
/// the trusted comment, which is what makes the comment trustworthy.</item>
/// </list>
/// Lines after the fourth are ignored.
/// </summary>
internal sealed class MinisignSignature
{
    const int Size = 2 + 8 + Ed25519.SignatureSize;
    const string TrustedPrefix = "trusted comment: ";

    MinisignSignature(bool prehashed, byte[] keyId, byte[] signature, string trustedComment, byte[] globalSignature)
    {
        Prehashed = prehashed;
        KeyId = keyId;
        Signature = signature;
        TrustedComment = trustedComment;
        GlobalSignature = globalSignature;
    }

    /// <summary>
    /// Whether the signature is over the file's BLAKE2b-512 hash ("ED", what
    /// minisign makes now) rather than over the file itself ("Ed", the legacy
    /// kind, still taken as Tack 0.2 took it).
    /// </summary>
    public bool Prehashed { get; }

    /// <summary>The id of the key that made the signature.</summary>
    public byte[] KeyId { get; }

    /// <summary>The Ed25519 signature over the file or its hash.</summary>
    public byte[] Signature { get; }

    /// <summary>The trusted comment, without its "trusted comment: " prefix. Unverified until the global signature is checked.</summary>
    public string TrustedComment { get; }

    /// <summary>The Ed25519 signature over <see cref="Signature"/> followed by the trusted comment's UTF-8.</summary>
    public byte[] GlobalSignature { get; }

    /// <summary>The signature in <paramref name="text"/>, or null when it is not one.</summary>
    public static MinisignSignature? Parse(string text)
    {
        var lines = TextLines.Split(text);
        if (lines.Count < 4
            || StrictBase64.Decode(lines[1]) is not { Length: Size } bin
            || StrictBase64.Decode(lines[3]) is not { Length: Ed25519.SignatureSize } global
            || !lines[2].StartsWith(TrustedPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // "Ed" is the legacy kind, "ED" the prehashed one; nothing else is minisign.
        if (bin[0] != 'E' || (bin[1] != 'd' && bin[1] != 'D'))
        {
            return null;
        }

        return new MinisignSignature(bin[1] == 'D', bin[2..10], bin[10..], lines[2][TrustedPrefix.Length..], global);
    }
}
