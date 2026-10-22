namespace Tack.Core.Phone;

/// <summary>The body of the phone page under its switch.</summary>
/// <param name="Kind">Which one.</param>
/// <param name="Text">Its main words: the explanation, the error, or for <see cref="PhoneBodyKind.Share"/> the line under the code.</param>
/// <param name="Address">Where a phone opens the board, for <see cref="PhoneBodyKind.Share"/>.</param>
/// <param name="Qr">The address as a QR code, when there is one, for <see cref="PhoneBodyKind.Share"/>.</param>
public sealed record PhoneBody(PhoneBodyKind Kind, string Text, string? Address = null, QrMatrix? Qr = null);
