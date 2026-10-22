using System.Net;
using System.Text;
using Tack.Core.NetBird;
using Tack.Core.Phone.Http;
using Tack.Core.Phone.Serving;

namespace Tack.Core.Tests.Phone.Serving;

/// <summary>
/// The routes on "my-pc" with one phone, "pixel", connected: a fake app, a
/// manual clock, and requests built as the page sends them.
/// </summary>
sealed class PhoneBench
{
    public const string PixelKey = "cGl4ZWw=";
    public const string MyHost = "my-pc.netbird.cloud:7717";

    public static readonly IPAddress MyIp = IPAddress.Parse("100.90.0.1");
    public static readonly Peer Pixel = new(IPAddress.Parse("100.90.0.2"), "pixel.netbird.cloud", PixelKey);
    public static readonly NetBirdStatus Me = new(MyIp, "my-pc.netbird.cloud", [Pixel]);

    /// <summary>The header the page adds to every call.</summary>
    public static readonly KeyValuePair<string, string> Ours = new("X-Tack", "1");

    public static readonly PhoneFiles PageFiles = new("page"u8.ToArray(), "script"u8.ToArray(), "style"u8.ToArray(), [0x89, 0x50]);

    public PhoneBench(NetBirdStatus? booked = null)
    {
        Peers = new PeerBook(booked ?? Me, () => Task.FromResult<NetBirdStatus?>(null), Time);
        Limits = new PinLimits(Time);
        Routes = new PhoneRoutes(Host, PageFiles, Peers, Limits);
    }

    public FakeHost Host { get; } = new();

    public ManualTime Time { get; } = new();

    public PeerBook Peers { get; }

    public PinLimits Limits { get; }

    public PhoneRoutes Routes { get; }

    /// <summary>A request with Host naming this PC unless the test gives a Host of its own.</summary>
    public static RequestHead Request(string method, string path, params KeyValuePair<string, string>[] headers)
    {
        var all = headers.Any(h => h.Key == "Host") ? headers : [new("Host", MyHost), .. headers];
        var length = all.FirstOrDefault(h => h.Key == "Content-Length").Value is { } text ? long.Parse(text) : 0;
        return new RequestHead(method, path, all, length, []);
    }

    public static KeyValuePair<string, string> Header(string name, string value) => new(name, value);

    public Task<PhoneResponse> Send(RequestHead head, IRequestBody? body = null) =>
        Routes.HandleAsync(head, body ?? FakeBody.None, new PeerMatch(Me, Pixel));

    public Task<PhoneResponse> Get(string path, params KeyValuePair<string, string>[] headers) =>
        Send(Request("GET", path, headers));

    /// <summary>A pin from the page: X-Tack, the type and the length, and the body.</summary>
    public Task<PhoneResponse> Pin(string type, byte[] bytes, FakeBody? body = null) =>
        Send(Request("POST", "/api/pin", Ours, Header("Content-Type", type), Header("Content-Length", $"{bytes.Length}")),
            body ?? new FakeBody(bytes));

    public static string Text(PhoneResponse response) => Encoding.UTF8.GetString(response.Body);
}
