namespace Tack.Core.Pinboard.Input;

/// <summary>
/// One wheel or touchpad step, read: how far it moves the row (px, positive
/// towards the row's end) and whether it is a wheel's notch, which glides,
/// rather than a touchpad's fine step, which is followed almost directly.
/// </summary>
public readonly record struct WheelTurn(double Delta, bool Notch);
