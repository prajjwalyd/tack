using Tack.Core.Phone.Http;

namespace Tack.Windows.Phone;

/// <summary>
/// How long each part of a connection may take. The app always uses
/// <see cref="Default"/>; tests shorten them so a stall is seen in moments.
/// </summary>
/// <param name="Head">The whole request head.</param>
/// <param name="BodyIdle">A body's stall between reads.</param>
/// <param name="BodyTotal">A whole body.</param>
/// <param name="Write">Each write of a response.</param>
sealed record ConnectionTimeouts(TimeSpan Head, TimeSpan BodyIdle, TimeSpan BodyTotal, TimeSpan Write)
{
    public static readonly ConnectionTimeouts Default = new(
        HttpLimits.HeadTimeout, HttpLimits.BodyIdle, HttpLimits.BodyTotal, HttpLimits.WriteTimeout);
}
