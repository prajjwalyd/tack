using Tack.Core.NetBird;
using Tack.Core.Phone.Http;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// What the phone board serves, and to whom. The page and its files go to
/// any NetBird peer; everything under /api/ needs a device the user allowed. Every request must name this PC in
/// Host (421 otherwise: DNS rebinding), a request with an Origin must come
/// from this page (403), and API calls that act or may ask the user must carry
/// X-Tack: 1, which another site open on the phone cannot add without a CORS
/// preflight this server never answers. Without it, an API request is served
/// only to a device already allowed, and never asks.
/// </summary>
public sealed class PhoneRoutes
{
    const string ThumbPrefix = "/api/thumb/";
    const string PrintPrefix = "/api/print/";

    /// <summary>Lets Android's "Add to Home screen" use Tack's name and icon.</summary>
    static readonly byte[] Manifest =
        """{"name":"Tack","short_name":"Tack","start_url":"/","display":"standalone","background_color":"#153328","theme_color":"#1c4234","icons":[{"src":"/icon.png","sizes":"256x256","type":"image/png"}]}"""u8
            .ToArray();

    readonly IPhoneHost _host;
    readonly PhoneFiles _files;
    readonly PeerBook _peers;
    readonly PinRoute _pin;

    public PhoneRoutes(IPhoneHost host, PhoneFiles files, PeerBook peers, PinLimits limits)
    {
        (_host, _files, _peers) = (host, files, peers);
        _pin = new PinRoute(host, limits);
    }

    /// <summary>The page may load only itself.</summary>
    public const string Csp =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; " +
        "connect-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>The largest pin: a photo the page has already scaled down, or a screenshot sent as it is.</summary>
    public const long MaxPin = 20 * 1024 * 1024;

    /// <summary>The largest picture file served to the viewer.</summary>
    public const long MaxServed = 64 * 1024 * 1024;

    /// <summary>The answer to one request from <paramref name="from"/>, whose body is read only if the route wants it.</summary>
    public async Task<PhoneResponse> HandleAsync(RequestHead head, IRequestBody body, PeerMatch from)
    {
        var host = AsciiCase.Lower(head.Header("Host") ?? string.Empty);
        if (!NamesMe(host, from.Me))
        {
            return PhoneResponse.Empty(421);
        }
        if (head.Header("Origin") is { } origin && !AsciiCase.Same(origin, $"http://{host}"))
        {
            return PhoneResponse.Empty(403);
        }
        var ours = head.Header("X-Tack") == "1";
        return head.Method switch
        {
            "GET" => await GetAsync(head, from, ours).ConfigureAwait(false),
            "POST" => await PostAsync(head, body, from, ours).ConfigureAwait(false),
            _ => PhoneResponse.Empty(405),
        };
    }

    /// <summary>Whether Host, lower-cased, names this PC by its NetBird name or address, on the board's port.</summary>
    static bool NamesMe(string host, NetBirdStatus me) =>
        host == $"{AsciiCase.Lower(me.Fqdn)}:{PhoneAddress.Port}" || host == $"{me.Ip}:{PhoneAddress.Port}";

    async Task<PhoneResponse> GetAsync(RequestHead head, PeerMatch from, bool ours)
    {
        var path = head.Path;
        switch (path)
        {
            case "/":
                return PhoneResponse.Bytes(200, "text/html; charset=utf-8", _files.Page);
            case "/phone.js":
                return PhoneResponse.Bytes(200, "text/javascript; charset=utf-8", _files.Script);
            case "/phone.css":
                return PhoneResponse.Bytes(200, "text/css; charset=utf-8", _files.Style);
            case "/icon.png":
                return PhoneResponse.Bytes(200, "image/png", _files.Icon);
            case "/manifest.webmanifest":
                return PhoneResponse.Bytes(200, "application/manifest+json", Manifest);
            case "/api/board":
                var verdict = await GateAsync(from.Peer, ours).ConfigureAwait(false);
                return verdict is Verdict.Allowed
                    ? BoardResponse.Of(_host.Prints(), PhoneAddress.PcName(from.Me), from.Peer.ShortName,
                        head.Header("If-None-Match"))
                    : Refusal(verdict, from);
        }
        if (path.StartsWith(ThumbPrefix, StringComparison.Ordinal))
        {
            return await GateAsync(from.Peer, ours: false).ConfigureAwait(false) is Verdict.Allowed
                ? PictureResponse.Thumb(_host.Find(path[ThumbPrefix.Length..]))
                : PhoneResponse.Empty(403);
        }
        if (path.StartsWith(PrintPrefix, StringComparison.Ordinal))
        {
            return await GateAsync(from.Peer, ours: false).ConfigureAwait(false) is Verdict.Allowed
                ? await PictureResponse.FullAsync(_host, _host.Find(path[PrintPrefix.Length..])).ConfigureAwait(false)
                : PhoneResponse.Empty(403);
        }
        return PhoneResponse.Empty(404);
    }

    async Task<PhoneResponse> PostAsync(RequestHead head, IRequestBody body, PeerMatch from, bool ours)
    {
        var path = head.Path;
        if (ours && path == "/api/ask")
        {
            var verdict = _host.AskAgain(from.Peer);
            return verdict is Verdict.Allowed
                ? PhoneResponse.Json(200, PhoneJson.Of(GateView.Allowed))
                : Refusal(verdict, from);
        }
        if (ours && path == "/api/pin")
        {
            var verdict = await GateAsync(from.Peer, ours: true).ConfigureAwait(false);
            return verdict is Verdict.Allowed
                ? await _pin.HandleAsync(head, body, from.Peer).ConfigureAwait(false)
                : Refusal(verdict, from);
        }
        return PhoneResponse.Empty(path.StartsWith("/api/", StringComparison.Ordinal) ? 403 : 404);
    }

    /// <summary>
    /// Whether <paramref name="peer"/> may use the board, from a fresh look at
    /// NetBird's peers: the address must still be that device's. Only a request
    /// from the page itself (<paramref name="ours"/>) may ask the user.
    /// </summary>
    async Task<Verdict> GateAsync(Peer peer, bool ours)
    {
        var current = await _peers.FindAsync(peer.Ip, fresh: true).ConfigureAwait(false);
        if (current is null || current.Peer.Key != peer.Key)
        {
            return Verdict.Refused.Instance;
        }
        if (ours)
        {
            return _host.Admit(peer);
        }
        return _host.Allows(peer.Key) ? Verdict.Allowed.Instance : Verdict.Refused.Instance;
    }

    /// <summary>Not (yet) allowed: what the page shows instead of the board.</summary>
    static PhoneResponse Refusal(Verdict verdict, PeerMatch from) =>
        PhoneResponse.Json(403, PhoneJson.Of(GateView.Refusal(verdict, PhoneAddress.PcName(from.Me), from.Peer.ShortName)));
}
