namespace Tack.Core.Phone;

/// <summary>
/// A QR code as its square of modules, dark or light, without the quiet zone
/// round it: what the phone page draws, black on white in every theme.
/// </summary>
public sealed class QrMatrix
{
    readonly bool[] _dark;

    /// <param name="size">Modules along a side.</param>
    /// <param name="dark">Each module row by row, top left first; true is dark.</param>
    public QrMatrix(int size, IReadOnlyList<bool> dark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if (dark.Count != size * size)
        {
            throw new ArgumentException("A QR code has size × size modules.", nameof(dark));
        }
        Size = size;
        _dark = [.. dark];
    }

    /// <summary>Modules along a side.</summary>
    public int Size { get; }

    /// <summary>Whether the module in column <paramref name="x"/>, row <paramref name="y"/> is dark.</summary>
    public bool IsDark(int x, int y) => _dark[(y * Size) + x];
}
