namespace Tack.Core.Updates.Signing;

/// <summary>
/// A minisign public key, read from the text of its .pub file: an untrusted
/// comment line, then the base64 of 42 bytes, which are the algorithm ("Ed"),
/// an 8-byte key id and the 32-byte Ed25519 key.
/// </summary>
internal sealed class MinisignPublicKey
{
    const int Size = 2 + 8 + Ed25519.PublicKeySize;

    MinisignPublicKey(byte[] keyId, byte[] key)
    {
        KeyId = keyId;
        Key = key;
    }

    /// <summary>The 8 bytes a signature names its key by.</summary>
    public byte[] KeyId { get; }

    /// <summary>The Ed25519 public key.</summary>
    public byte[] Key { get; }

    /// <summary>The key in <paramref name="text"/>, or null when it is not one.</summary>
    public static MinisignPublicKey? Parse(string text)
    {
        // The first line is a comment nobody signed; it is not read.
        var lines = TextLines.Split(text);
        if (lines.Count < 2 || StrictBase64.Decode(lines[1]) is not { Length: Size } bin)
        {
            return null;
        }

        // A key states "Ed"; "ED" is taken too, as Tack 0.2 took it.
        if (bin[0] != 'E' || (bin[1] != 'd' && bin[1] != 'D'))
        {
            return null;
        }

        return new MinisignPublicKey(bin[2..10], bin[10..]);
    }
}
