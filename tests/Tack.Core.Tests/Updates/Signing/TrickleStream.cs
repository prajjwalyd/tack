namespace Tack.Core.Tests.Updates.Signing;

/// <summary>A stream that hands out at most one byte per read, as a slow network might.</summary>
public sealed class TrickleStream(byte[] data) : Stream
{
    int _at;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => data.Length;

    public override long Position
    {
        get => _at;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty || _at == data.Length)
        {
            return 0;
        }

        buffer[0] = data[_at++];
        return 1;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
