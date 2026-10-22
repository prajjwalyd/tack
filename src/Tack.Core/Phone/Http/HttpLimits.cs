namespace Tack.Core.Phone.Http;

/// <summary>
/// The phone server's hard limits, as Tack 0.2 had them: a small head, a body
/// only when a route wants one, and a timeout on every read and write, so a
/// device that stalls or floods holds nothing for long.
/// </summary>
public static class HttpLimits
{
    /// <summary>The most a request head may take, its blank line included.</summary>
    public const int MaxHead = 16 * 1024;

    /// <summary>The most headers a request may carry. The page sends about ten.</summary>
    public const int MaxHeaders = 32;

    /// <summary>How long a client may take to send its whole request head.</summary>
    public static readonly TimeSpan HeadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long a body may stall between reads (a photo over a slow relayed link).</summary>
    public static readonly TimeSpan BodyIdle = TimeSpan.FromSeconds(20);

    /// <summary>How long a body may take in all.</summary>
    public static readonly TimeSpan BodyTotal = TimeSpan.FromSeconds(120);

    /// <summary>How long each write of a response may take.</summary>
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(30);
}
