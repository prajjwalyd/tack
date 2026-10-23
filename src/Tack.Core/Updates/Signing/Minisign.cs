using System.Text;

namespace Tack.Core.Updates.Signing;

/// <summary>
/// Checks minisign signatures, the kind Tack's releases carry: Ed25519 over
/// the file's BLAKE2b-512 hash (or over the file itself, for the legacy
/// kind), plus a global signature over the trusted comment, which says which
/// file and version was signed.
/// </summary>
public static class Minisign
{
    /// <summary>Refuses invalid UTF-8 rather than replacing it, as Tack 0.2 did.</summary>
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    const int ReadSize = 81_920;

    /// <summary>
    /// The trusted comment, when <paramref name="signature"/> is a good
    /// signature by <paramref name="publicKey"/> over <paramref name="content"/>
    /// (read to its end); null for anything else: another key, a changed file
    /// or comment, or text that is not a signature.
    /// </summary>
    /// <remarks>
    /// Never throws for bad input; only an <see cref="IOException"/> from
    /// reading <paramref name="content"/> gets out. The content is read only
    /// once the key and signature have parsed and name the same key.
    /// </remarks>
    /// <param name="publicKey">The base64 of the key's .pub text, as <see cref="UpdateTrust.PublicKey"/>.</param>
    /// <param name="signature">The base64 of the .sig text, as latest.json carries it.</param>
    public static string? Verify(string publicKey, string signature, Stream content)
    {
        if (publicKey is null || signature is null || content is null
            || Unwrap(publicKey) is not { } keyText || Unwrap(signature) is not { } signatureText
            || MinisignPublicKey.Parse(keyText) is not { } key
            || MinisignSignature.Parse(signatureText) is not { } sig
            || !key.KeyId.AsSpan().SequenceEqual(sig.KeyId))
        {
            return null;
        }

        // What the signature covers: the content's BLAKE2b-512 hash, or for
        // the legacy kind the content itself.
        var signed = sig.Prehashed ? HashOf(content) : ReadAll(content);
        if (!Ed25519.Verify(key.Key, sig.Signature, signed))
        {
            return null;
        }

        // Only the global signature, over the signature and then the trusted
        // comment, makes the comment (and the version in it) trustworthy.
        byte[] global = [.. sig.Signature, .. Encoding.UTF8.GetBytes(sig.TrustedComment)];
        return Ed25519.Verify(key.Key, sig.GlobalSignature, global) ? sig.TrustedComment : null;
    }

    /// <summary>
    /// The version a verified trusted comment says the file was signed for:
    /// the <c>version:</c> field among its tab-separated fields
    /// ("timestamp:1700000000\tfile:Tack_0.3.0_x64-setup.exe\tversion:0.3.0"),
    /// or null when it names none.
    /// </summary>
    public static string? SignedVersion(string trustedComment)
    {
        if (trustedComment is null)
        {
            return null;
        }

        // The first field that starts "version:", as Tack 0.2 read it.
        const string Prefix = "version:";
        foreach (var field in trustedComment.Split('\t'))
        {
            if (field.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return field[Prefix.Length..];
            }
        }

        return null;
    }

    /// <summary>The text in a key or signature as latest.json and <see cref="UpdateTrust"/> carry it: base64 of UTF-8.</summary>
    static string? Unwrap(string base64)
    {
        if (StrictBase64.Decode(base64) is not { } bytes)
        {
            return null;
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    static byte[] HashOf(Stream content)
    {
        var hasher = new Blake2b();
        var buffer = new byte[ReadSize];
        int read;
        while ((read = content.Read(buffer)) > 0)
        {
            hasher.Update(buffer.AsSpan(0, read));
        }

        var hash = new byte[Blake2b.HashSize];
        hasher.Finish(hash);
        return hash;
    }

    static byte[] ReadAll(Stream content)
    {
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        return copy.ToArray();
    }
}
