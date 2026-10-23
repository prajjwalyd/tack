using System.Net.Http.Headers;
using Tack.Core.Updates;

namespace Tack.Windows.Updates;

/// <summary>
/// GET over https only. Redirects are followed by hand rather than by the
/// handler, so every hop can be held to <see cref="HttpsRedirects"/>: https,
/// at most five. Reaching a server may take 15 s; how long a whole request
/// may take is the caller's to say, through its cancellation token. The
/// system's proxy is used, as a browser would. Safe from any thread.
/// </summary>
public sealed class HttpsClient : IDisposable
{
    static readonly TimeSpan s_connect = TimeSpan.FromSeconds(15);

    readonly HttpClient _client;

    /// <param name="product">The User-Agent's product and version, as ("Tack", "0.3.0").</param>
    public HttpsClient(string product, string version)
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = s_connect };
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(product, version));
    }

    /// <summary>
    /// The answer to a GET of <paramref name="url"/>, once its headers are in,
    /// after any redirects. The caller reads the body and disposes the answer.
    /// </summary>
    /// <exception cref="UpdateFailureException">A URL or redirect that is not https, too many redirects, or an answer other than success.</exception>
    public async Task<HttpResponseMessage> GetAsync(Uri url, CancellationToken cancel)
    {
        if (!HttpsRedirects.IsHttps(url))
        {
            throw new UpdateFailureException(UpdateFailure.Server, "an address that is not https");
        }
        var at = url;
        for (var followed = 0; ; followed++)
        {
            var response = await _client.GetAsync(at, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (HttpsRedirects.IsRedirect(status))
            {
                var location = response.Headers.Location;
                response.Dispose();
                at = HttpsRedirects.Next(at, location, followed);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new UpdateFailureException(UpdateFailure.Server, $"the server answered {status}");
            }
            return response;
        }
    }

    public void Dispose() => _client.Dispose();
}
