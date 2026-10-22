using Tack.Core.Phone.Http;

namespace Tack.Core.Tests.Phone.Serving;

/// <summary>A request body handed in as bytes; null bytes stand for one that never arrives in full.</summary>
sealed class FakeBody(byte[]? bytes) : IRequestBody
{
    public static FakeBody None { get; } = new([]);

    /// <summary>The cap the route asked with, if it read the body at all.</summary>
    public long? AskedMax { get; private set; }

    public Task<byte[]?> ReadAsync(long max)
    {
        AskedMax = max;
        return Task.FromResult(bytes is { } b && b.LongLength <= max ? b : null);
    }
}
