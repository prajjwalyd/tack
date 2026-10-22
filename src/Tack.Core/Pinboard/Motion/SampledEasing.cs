namespace Tack.Core.Pinboard.Motion;

/// <summary>
/// An easing given as evenly spaced values, straight between them: how a
/// spring's step response is played. Outside 0 to 1 it holds the end values.
/// </summary>
public sealed class SampledEasing : Easing
{
    readonly double[] _points;
    readonly double[] _knots;

    /// <param name="points">The values at 0, 1 / (n - 1), ... 1; at least two.</param>
    public SampledEasing(IReadOnlyList<double> points)
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("An easing needs at least two points.", nameof(points));
        }
        _points = [.. points];
        _knots = [.. Enumerable.Range(0, _points.Length).Select(i => i / (double)(_points.Length - 1))];
    }

    public IReadOnlyList<double> Points => _points;

    public override IReadOnlyList<double> Knots => _knots;

    public override double At(double x)
    {
        if (x <= 0)
        {
            return _points[0];
        }
        if (x >= 1)
        {
            return _points[^1];
        }
        var at = x * (_points.Length - 1);
        var i = (int)Math.Floor(at);
        var f = at - i;
        return f == 0 ? _points[i] : _points[i] + (_points[i + 1] - _points[i]) * f;
    }
}
