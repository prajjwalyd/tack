namespace Tack.Windows.NetBird;

/// <summary>Reads a program's output up to a cap, so a misbehaving program cannot fill memory.</summary>
static class CappedOutput
{
    /// <summary>Everything up to the end of <paramref name="stream"/>, or null once it passes <paramref name="cap"/> bytes.</summary>
    public static async Task<byte[]?> ReadAsync(Stream stream, int cap)
    {
        using var output = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > cap)
            {
                return null;
            }
            output.Write(chunk, 0, read);
        }
        return output.ToArray();
    }
}
