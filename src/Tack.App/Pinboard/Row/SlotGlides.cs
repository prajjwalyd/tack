using System.Diagnostics;
using Tack.App.Pinboard.Model;
using Tack.App.Pinboard.Motion;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Row;

/// <summary>
/// Puts each print's slot in its place in the row, gliding it there on the
/// layout spring. The slot's element moves to its new place at once, so the
/// pointer finds it exactly where it will rest; its translation starts at the
/// distance back to where it was seen and glides to 0. A glide that is
/// retargeted carries on from where it is, as the row always did.
/// </summary>
sealed class SlotGlides(Keyframer keyframer)
{
    /// <summary>A glide running on a slot: its starting offset, when it started and how long it runs.</summary>
    sealed record Glide(double From, long Started, double Ms);

    readonly Dictionary<BoardItem, Glide> _glides = [];
    readonly HashSet<BoardItem> _placed = [];

    /// <summary>
    /// Places <paramref name="item"/>'s slot at <paramref name="x"/>, <paramref name="y"/>.
    /// A slot placed for the first time, or with <paramref name="glideMs"/> null, goes there at once.
    /// </summary>
    public void Place(BoardItem item, double x, double y, double? glideMs)
    {
        var first = _placed.Add(item);
        var from = item.X;
        (item.X, item.Y) = (x, y);
        item.View.PlaceAt(x, y);
        if (first || from == x)
        {
            return;
        }
        if (glideMs is not { } ms)
        {
            Stop(item);
            return;
        }
        // Still seen where it was: its old place plus whatever is left of a glide on the way there.
        var offset = Offset(item) + from - x;
        var motion = new Timeline(ms)
            .Key(0, new Pose { X = offset }, Spring.Layout.Easing)
            .Key(1, new Pose { X = 0 })
            .Sample();
        _glides[item] = new Glide(offset, Stopwatch.GetTimestamp(), ms);
        keyframer.Play(motion, item.View.Slot.Visual, () => _glides.Remove(item));
    }

    /// <summary>Puts every slot still gliding straight in its place (the motions were cancelled on tuck).</summary>
    public void Settle()
    {
        foreach (var item in _glides.Keys.ToArray())
        {
            Stop(item);
        }
    }

    /// <summary>The print left the row: it is never placed again.</summary>
    public void Forget(BoardItem item)
    {
        _placed.Remove(item);
        _glides.Remove(item);
    }

    void Stop(BoardItem item)
    {
        if (_glides.Remove(item))
        {
            keyframer.Set(item.View.Slot.Visual, Channel.X, 0);
        }
    }

    /// <summary>How far from its place the slot is now, by the glide's own curve.</summary>
    double Offset(BoardItem item)
    {
        if (!_glides.TryGetValue(item, out var glide))
        {
            return 0;
        }
        var t = Stopwatch.GetElapsedTime(glide.Started).TotalMilliseconds / glide.Ms;
        return t >= 1 ? 0 : glide.From * (1 - Spring.Layout.Easing.At(Math.Max(0, t)));
    }
}
