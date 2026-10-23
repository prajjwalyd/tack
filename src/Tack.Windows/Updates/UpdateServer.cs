using System.Text;
using Tack.Core.Updates;
using Tack.Core.Updates.Feed;

namespace Tack.Windows.Updates;

/// <summary>
/// The network side of updating, over <see cref="HttpsClient"/>: fetching
/// latest.json (the whole check at most 30 s) and downloading an installer
/// into a fresh folder (the whole download at most 10 min, so a stalled one
/// ends). Safe from any thread; each call runs on the thread pool once it waits.
/// </summary>
public sealed class UpdateServer(string product, string version) : IDisposable
{
    static readonly TimeSpan s_check = TimeSpan.FromSeconds(30);
    static readonly TimeSpan s_download = TimeSpan.FromMinutes(10);

    /// <summary>More than this is not latest.json, which is a few kilobytes.</summary>
    const int FeedMaxBytes = 1024 * 1024;

    readonly HttpsClient _https = new(product, version);

    /// <summary>The text of the feed at <paramref name="url"/>.</summary>
    public async Task<string> FeedAsync(Uri url, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(s_check);
        using var response = await _https.GetAsync(url, timeout.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > FeedMaxBytes)
        {
            throw new UpdateFailureException(UpdateFailure.Server, "latest.json is far too large");
        }
        var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using (body.ConfigureAwait(false))
        {
            using var kept = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (kept.Length + read > FeedMaxBytes)
                {
                    throw new UpdateFailureException(UpdateFailure.Server, "latest.json is far too large");
                }
                kept.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(kept.GetBuffer(), 0, (int)kept.Length);
        }
    }

    /// <summary>
    /// Downloads the installer at <paramref name="url"/> into a fresh folder
    /// under %TEMP% and returns its path; on failure the folder goes again.
    /// <paramref name="onPercent"/> hears each new whole percent, when the size is known.
    /// </summary>
    public async Task<string> DownloadAsync(Uri url, Action<int> onPercent, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(s_download);
        var file = Path.Combine(InstallerFolder.Fresh(), UpdateFeed.InstallerName(url));
        try
        {
            using var response = await _https.GetAsync(url, timeout.Token).ConfigureAwait(false);
            var total = response.Content.Headers.ContentLength;
            var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                var saved = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, useAsync: true);
                await using (saved.ConfigureAwait(false))
                {
                    await Copy(body, saved, total, onPercent, timeout.Token).ConfigureAwait(false);
                }
            }
            return file;
        }
        catch
        {
            InstallerFolder.Discard(file);
            throw;
        }
    }

    public void Dispose() => _https.Dispose();

    static async Task Copy(Stream from, Stream to, long? total, Action<int> onPercent, CancellationToken cancel)
    {
        var buffer = new byte[80 * 1024];
        var (bytes, shown) = (0L, (int?)null);
        int read;
        while ((read = await from.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
            bytes += read;
            if (DownloadPercent.Of(bytes, total) is { } percent && percent != shown)
            {
                shown = percent;
                onPercent(percent);
            }
        }
        if (total is { } expected && bytes != expected)
        {
            throw new UpdateFailureException(UpdateFailure.Network, $"the download ended at {bytes} of {expected} bytes");
        }
    }
}
