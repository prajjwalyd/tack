namespace Tack.Core.Phone.Qr;

/// <summary>
/// The square being drawn: each module dark or light, and whether it belongs
/// to a function pattern, which data and masks leave alone.
/// </summary>
internal sealed class QrCanvas(int size)
{
    readonly bool[] _dark = new bool[size * size];
    readonly bool[] _function = new bool[size * size];

    /// <summary>Modules along a side.</summary>
    public int Size => size;

    /// <summary>Whether the module in column <paramref name="x"/>, row <paramref name="y"/> is dark.</summary>
    public bool IsDark(int x, int y) => _dark[(y * size) + x];

    /// <summary>Draws a module of a function pattern.</summary>
    public void SetFunction(int x, int y, bool dark)
    {
        _dark[(y * size) + x] = dark;
        _function[(y * size) + x] = true;
    }

    /// <summary>
    /// Draws the codewords' bits, most significant first, up and down column
    /// pairs from the right, skipping function modules and the timing column.
    /// Modules left over stay light.
    /// </summary>
    public void Place(byte[] codewords)
    {
        var bit = 0;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }
            var upward = ((right + 1) & 2) == 0;
            for (var step = 0; step < size; step++)
            {
                var y = upward ? size - 1 - step : step;
                for (var x = right; x >= right - 1; x--)
                {
                    if (!_function[(y * size) + x] && bit < codewords.Length * 8)
                    {
                        _dark[(y * size) + x] = ((codewords[bit / 8] >> (7 - (bit % 8))) & 1) != 0;
                        bit++;
                    }
                }
            }
        }
    }

    /// <summary>Flips the data modules <paramref name="mask"/> picks; flipping twice undoes it.</summary>
    public void Flip(int mask)
    {
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (!_function[(y * size) + x] && QrMask.Flips(mask, x, y))
                {
                    _dark[(y * size) + x] ^= true;
                }
            }
        }
    }

    /// <summary>The finished code.</summary>
    public QrMatrix ToMatrix() => new(size, _dark);
}
