using System.Diagnostics;
using Tack.App.Pinboard.Model;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// Prints swinging on their pins, on their swing layers: a nudge adds to a
/// swing already going, so nudges blend. No swings under reduced motion, on a
/// leaving print or while tucked. UI thread only.
/// </summary>
sealed class Swings(BoardContext board)
{
    readonly Dictionary<BoardItem, (Swing Swing, long Start, Playing Playing)> _going = [];

    /// <summary>Pushes <paramref name="item"/> to peak at about <paramref name="amplitude"/> degrees.</summary>
    public void Nudge(BoardItem item, double amplitude)
    {
        if (board.State.Reduced || item.Leaving || !board.State.Revealed)
        {
            return;
        }
        var now = Stopwatch.GetTimestamp();
        Swing? current = null;
        double elapsed = 0;
        if (_going.TryGetValue(item, out var going) && !going.Playing.Over)
        {
            (current, elapsed) = (going.Swing, Stopwatch.GetElapsedTime(going.Start, now).TotalMilliseconds);
        }
        var swing = Swing.Nudge(amplitude, board.Clock.Scale, current, elapsed);
        Playing? playing = null;
        playing = board.Keyframer.Play(swing.Timeline().Sample(), item.View.Swing.Visual, () =>
        {
            if (_going.TryGetValue(item, out var mine) && mine.Playing == playing)
            {
                _going.Remove(item);
            }
        });
        _going[item] = (swing, now, playing);
    }

    /// <summary>Stops <paramref name="item"/>'s swing where it is (it is leaving, and its fall takes over).</summary>
    public void Stop(BoardItem item)
    {
        if (_going.Remove(item, out var going))
        {
            going.Playing.Cancel();
        }
    }

    /// <summary>Every swing stops, hanging straight again: the board has tucked, or less motion is asked for.</summary>
    public void Clear()
    {
        foreach (var (item, going) in _going)
        {
            going.Playing.Cancel();
            board.Keyframer.Set(item.View.Swing.Visual, Channel.Rotation, 0);
        }
        _going.Clear();
    }
}
