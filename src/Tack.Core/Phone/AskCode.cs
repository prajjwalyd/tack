using System.Globalization;
using System.Security.Cryptography;

namespace Tack.Core.Phone;

/// <summary>
/// The code shown on the PC and on the device for one request, "0427". It only
/// has to differ between requests, not be secret: what it proves is that the
/// question on the PC belongs to the phone in the person's hand.
/// </summary>
public static class AskCode
{
    /// <summary>A fresh four-digit code.</summary>
    public static string Fresh() => RandomNumberGenerator.GetInt32(10_000).ToString("D4", CultureInfo.InvariantCulture);
}
