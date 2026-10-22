namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// One frame of a flight's stand-in: at <paramref name="Offset"/> of the
/// flight, its transform (translate, rotate, then scale, about the pin point)
/// and its opacity.
/// </summary>
public readonly record struct FlightFrame(
    double Offset, double X, double Y, double Rotation, double ScaleX, double ScaleY, double Opacity);
