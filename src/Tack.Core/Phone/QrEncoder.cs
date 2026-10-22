using System.Text;
using Tack.Core.Phone.Qr;

namespace Tack.Core.Phone;

/// <summary>
/// Makes the QR code the phone card shows for the board's address, as Tack 0.2
/// made it: error correction level M, the text (UTF-8) split into digit,
/// alphanumeric and byte segments, the smallest version that holds them, and
/// the mask with the lowest penalty. Every version, 1 to 40, is supported.
/// </summary>
public static class QrEncoder
{
    /// <summary>
    /// The code for <paramref name="text"/> (UTF-8); null when it is too long for
    /// version 40, which holds 2,331 bytes of text, or more when it is mostly digits.
    /// </summary>
    public static QrMatrix? Encode(string text)
    {
        if (QrPayload.Of(Encoding.UTF8.GetBytes(text)) is not { } payload)
        {
            return null;
        }
        var canvas = new QrCanvas(QrVersion.Size(payload.Version));
        QrFunctionPatterns.Draw(canvas, payload.Version);
        canvas.Place(QrBlocks.Interleave(payload.Codewords, payload.Version));

        var (best, lowest) = (0, int.MaxValue);
        for (var mask = 0; mask < QrMask.Count; mask++)
        {
            canvas.Flip(mask);
            QrFunctionPatterns.DrawFormat(canvas, mask);
            var penalty = QrPenalty.Score(canvas);
            if (penalty < lowest)
            {
                (best, lowest) = (mask, penalty);
            }
            canvas.Flip(mask);
        }
        canvas.Flip(best);
        QrFunctionPatterns.DrawFormat(canvas, best);
        return canvas.ToMatrix();
    }
}
