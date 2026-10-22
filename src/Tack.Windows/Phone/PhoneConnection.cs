using System.Net.Sockets;
using System.Text;
using Tack.Core.Phone.Http;

namespace Tack.Windows.Phone;

/// <summary>
/// One connection carrying one request: its head read within the head
/// timeout, its body (only when the route asks) within the body's, the
/// response sent with a timeout on every write, then the socket shut down
/// both ways and closed. A client that goes away or stalls just ends the
/// connection: reads and writes give up quietly rather than throw.
/// </summary>
sealed class PhoneConnection : IRequestBody, IDisposable
{
    const int Chunk = 64 * 1024;

    readonly Socket _socket;
    readonly NetworkStream _stream;
    readonly ConnectionTimeouts _timeouts;
    RequestHead? _head;
    bool _bodyRead;

    public PhoneConnection(Socket socket, ConnectionTimeouts timeouts)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _timeouts = timeouts;
    }

    /// <summary>
    /// The request's head, or why there is none: Gone when the client closes,
    /// fails, or has not sent a whole head when the head timeout is up.
    /// </summary>
    public async Task<HeadParse> ReadHeadAsync()
    {
        // A head never needs more: the parser refuses one that fills this.
        var buffer = new byte[HttpLimits.MaxHead];
        var filled = 0;
        using var deadline = new CancellationTokenSource(_timeouts.Head);
        while (true)
        {
            int read;
            try
            {
                read = await _stream.ReadAsync(buffer.AsMemory(filled), deadline.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (IsGone(e))
            {
                return HeadParse.Failed(HeadError.Gone);
            }
            if (read == 0)
            {
                return HeadParse.Failed(HeadError.Gone);
            }
            filled += read;
            var parse = HeadParser.Parse(buffer.AsSpan(0, filled));
            if (!parse.IsPartial)
            {
                _head = parse.Head;
                return parse;
            }
        }
    }

    /// <summary>
    /// The body, read once: null when Content-Length is over <paramref name="max"/>
    /// (and then nothing is read), or when the body stalls, runs out of time or
    /// the client closes before it is all here. Expect: 100-continue is answered
    /// first; the bytes that came with the head count towards it.
    /// </summary>
    public async Task<byte[]?> ReadAsync(long max)
    {
        if (_head is not { } head || _bodyRead || head.ContentLength > max || head.ContentLength > Array.MaxLength)
        {
            return null;
        }
        _bodyRead = true;
        try
        {
            if (string.Equals(head.Header("Expect"), "100-continue", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(Encoding.ASCII.GetBytes(ResponseHead.Continue)).ConfigureAwait(false);
            }
            var body = new byte[head.ContentLength];
            var filled = (int)Math.Min(head.EarlyBody.Length, head.ContentLength);
            head.EarlyBody.AsSpan(0, filled).CopyTo(body);
            using var total = new CancellationTokenSource(_timeouts.BodyTotal);
            while (filled < body.Length)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
                idle.CancelAfter(_timeouts.BodyIdle);
                var want = Math.Min(body.Length - filled, Chunk);
                var read = await _stream.ReadAsync(body.AsMemory(filled, want), idle.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    return null;
                }
                filled += read;
            }
            return body;
        }
        catch (Exception e) when (IsGone(e))
        {
            return null;
        }
    }

    /// <summary>
    /// Sends <paramref name="response"/> with the headers every response carries.
    /// A file body is streamed from disk, exactly its Length bytes (fewer only if
    /// the file is cut short meanwhile, and then the client sees it was); a file
    /// that cannot be opened is answered 404 instead. False when the client went
    /// away or a write ran out of time.
    /// </summary>
    public async Task<bool> SendAsync(PhoneResponse response, string csp)
    {
        var file = response.FilePath is { } path ? Open(path) : null;
        if (response.FilePath != null && file == null)
        {
            response = PhoneResponse.Empty(404);
        }
        await using (file)
        {
            try
            {
                await WriteAsync(Encoding.UTF8.GetBytes(ResponseHead.Text(response, csp))).ConfigureAwait(false);
                if (file != null)
                {
                    await CopyAsync(file, response.Length).ConfigureAwait(false);
                }
                else if (response.Body.Length > 0)
                {
                    await WriteAsync(response.Body).ConfigureAwait(false);
                }
                return true;
            }
            catch (Exception e) when (IsGone(e))
            {
                return false;
            }
        }
    }

    /// <summary>Shuts the socket down both ways, as each connection ends, and closes it.</summary>
    public void Dispose()
    {
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            // Already closed by the client.
        }
        _stream.Dispose();
        _socket.Dispose();
    }

    async Task CopyAsync(FileStream file, long length)
    {
        var chunk = new byte[(int)Math.Clamp(length, 1, Chunk)];
        var left = length;
        while (left > 0)
        {
            var read = await file.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, left))).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }
            await WriteAsync(chunk.AsMemory(0, read)).ConfigureAwait(false);
            left -= read;
        }
    }

    async Task WriteAsync(ReadOnlyMemory<byte> bytes)
    {
        using var deadline = new CancellationTokenSource(_timeouts.Write);
        await _stream.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// The file for reading, shared so a save or a delete under way is not held
    /// up; null when it cannot be opened.
    /// </summary>
    static FileStream? Open(string path)
    {
        try
        {
            return new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>What a client going away, a read or write running out of time, or a file failing mid-way throws.</summary>
    static bool IsGone(Exception e) =>
        e is OperationCanceledException or IOException or SocketException or ObjectDisposedException;
}
