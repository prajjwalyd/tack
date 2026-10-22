using Microsoft.UI.Composition;
using Tack.App.Pinboard.Motion;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Motion;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// A print's looks for what it is doing, each eased in from wherever it is:
/// at rest on its tilt; hovered, lifted and turned most of the way straight,
/// leaning toward the pointer, its lift shadow, sheen, buttons and caption
/// shown; pressed, sinking under a held press; dragged out, peeled up from its
/// free edge and faint; kept, its pin brass and larger. Under reduced motion
/// the card keeps its tilt and only fades. Nothing eases while the board is
/// up: looks are put in place at once. UI thread only.
/// </summary>
sealed class PrintStates(PrintView view, PrintParts parts, BoardContext? board, Keyframer keyframer)
{
    const double HoverTilt = 0.2;
    const double LeanMost = 0.9;
    const double PeelAngle = 16;
    const double SheenRestY = -0.7;
    const double PressMs = 520;
    static readonly CubicBezier PressCurve = new(0.3, 0, 0.2, 1);
    static readonly Easing Ease = CubicBezier.Ease;
    static readonly Easing Out = CubicBezier.QuickOut;

    readonly Dictionary<(Visual, Channel), (double Target, Playing? Playing)> _targets = [];
    double _lean;
    double _nx;
    double _ny = SheenRestY;
    bool _animate;

    /// <summary>The resting tilt, degrees.</summary>
    public double Tilt { get; set; }

    bool Reduced => board?.State.Reduced ?? false;

    /// <summary>Puts every look where the print's state says, eased when <paramref name="animate"/> and the board is down.</summary>
    public void Apply(bool animate = true)
    {
        _animate = animate && board is { State.Revealed: true };
        var (hover, press, drag) = (view.Hovered, view.Pressing, view.Dragging);
        if (!hover)
        {
            (_lean, _nx, _ny) = (0, 0, SheenRestY);
        }
        Card(hover, press, drag);
        Shade(hover, press, drag);
        var shown = hover && !press && !drag;
        Button(view.KeepButton.Visual, parts.Keep, shown);
        Button(view.XButton.Visual, parts.Unpin, shown);
        view.KeepButton.Element.IsHitTestVisible = view.XButton.Element.IsHitTestVisible = shown;
        var caption = view.Caption.Visual;
        if (hover && !drag)
        {
            Go(caption, Channel.Opacity, 1, Ms(170), Ease, Ms(120));
            Go(caption, Channel.Y, 0, Ms(170), Out, Ms(120));
        }
        else
        {
            Go(caption, Channel.Opacity, 0, Ms(120), Ease);
            Go(caption, Channel.Y, -2, Ms(120), Ease);
        }
        var sheen = hover && !press && !Reduced;
        Go(parts.Sheen, Channel.Opacity, sheen ? 1 : 0, Ms(sheen ? 190 : Spring.Settle.Ms), Ease);
        MoveSheen();
        if (drag)
        {
            Go(parts.Curl, Channel.Opacity, 1, Ms(160), Ease);
        }
        else
        {
            Go(parts.Curl, Channel.Opacity, 0, 0, Ease);
        }
    }

    /// <summary>The pointer over the hovered print, each -1 to 1 across its slot: the free end leans toward it, more the lower it is, and the sheen follows.</summary>
    public void Lean(double nx, double ny)
    {
        if (Reduced || !view.Hovered || view.Pressing || view.Dragging)
        {
            return;
        }
        _animate = board is { State.Revealed: true };
        (_nx, _ny) = (Math.Clamp(nx, -1, 1), Math.Clamp(ny, -1, 1));
        _lean = -_nx * LeanMost * (0.35 + 0.65 * (_ny + 1) / 2);
        Go(view.Card.Visual, Channel.Rotation, Tilt * HoverTilt + _lean, Ms(220), Out);
        MoveSheen();
    }

    /// <summary>Kept: the brass head fades in over the plain one and the pin grows; <paramref name="animate"/> eases both (the size only with full motion).</summary>
    public void Keep(bool kept, bool animate)
    {
        _animate = animate && board is { State.Revealed: true };
        Go(view.Brass.Visual, Channel.Opacity, kept ? 1 : 0, Ms(180), Ease);
        Go(parts.PinBody, Channel.Scale, kept ? BoardMetrics.KeptPinScale : 1, Reduced ? 0 : Ms(180), Ease);
        parts.Keep.SetKept(kept);
    }

    /// <summary>A shown button held down shrinks a little, and springs back when let go.</summary>
    public void ButtonPressed(PrintLayer button, bool down)
    {
        _animate = board is { State.Revealed: true };
        if (button.Element.IsHitTestVisible)
        {
            Go(button.Visual, Channel.Scale, down ? 0.88 : 1, Ms(down ? 80 : 140), Out);
        }
    }

    void Card(bool hover, bool press, bool drag)
    {
        var card = view.Card.Visual;
        if (Reduced)
        {
            Go(card, Channel.Y, 0, 0, Ease);
            Go(card, Channel.Scale, 1, 0, Ease);
            Go(card, Channel.Rotation, Tilt, 0, Ease);
            Go(parts.Peel, Channel.Rotation, 0, 0, Ease);
            Go(card, Channel.Opacity, drag ? 0.45 : 1, Ms(150), Ease);
            return;
        }
        if (drag)
        {
            var peel = Ms(260);
            Go(card, Channel.Y, -3, peel, Out);
            Go(card, Channel.Scale, 1, peel, Out);
            Go(card, Channel.Rotation, Tilt * HoverTilt, peel, Out);
            Go(parts.Peel, Channel.Rotation, PeelAngle, peel, Out);
            Go(card, Channel.Opacity, 0.45, Ms(220), Ease, Ms(140));
            return;
        }
        Go(card, Channel.Opacity, 1, Ms(220), Ease);
        if (press)
        {
            var ms = _animate ? MotionClock.Fixed(PressMs, "the press and hold is a gesture's time") : 0;
            Go(card, Channel.Y, 0, ms, PressCurve);
            Go(card, Channel.Scale, 0.97, ms, PressCurve);
            Go(card, Channel.Rotation, Tilt * HoverTilt, Ms(200), Ease);
            Go(parts.Peel, Channel.Rotation, 0, Ms(200), Ease);
        }
        else if (hover)
        {
            Go(card, Channel.Y, -2, Ms(190), Out);
            Go(card, Channel.Scale, 1, Ms(190), Out);
            Go(card, Channel.Rotation, Tilt * HoverTilt + _lean, Ms(220), Out);
            Go(parts.Peel, Channel.Rotation, 0, Ms(220), Out);
        }
        else
        {
            var settle = Ms(Spring.Settle.Ms);
            Go(card, Channel.Y, 0, settle, Spring.Settle.Easing);
            Go(card, Channel.Scale, 1, settle, Spring.Settle.Easing);
            Go(card, Channel.Rotation, Tilt, settle, Spring.Settle.Easing);
            Go(parts.Peel, Channel.Rotation, 0, settle, Spring.Settle.Easing);
        }
    }

    void Shade(bool hover, bool press, bool drag)
    {
        var shade = view.Shade.Visual;
        if (drag)
        {
            Go(shade, Channel.Opacity, 1, Ms(160), Ease);
            Go(shade, Channel.Y, Reduced ? 0 : 5, Ms(260), Out);
            Go(shade, Channel.Scale, Reduced ? 1 : 1.02, Ms(260), Out);
            return;
        }
        Go(shade, Channel.Y, 0, 0, Ease);
        Go(shade, Channel.Scale, 1, 0, Ease);
        if (press)
        {
            Go(shade, Channel.Opacity, 0, _animate ? MotionClock.Fixed(PressMs, "the press and hold is a gesture's time") : 0, Ease);
        }
        else
        {
            Go(shade, Channel.Opacity, hover ? 1 : 0, Ms(hover ? 190 : Spring.Settle.Ms), Ease);
        }
    }

    void Button(Visual button, PrintButton look, bool shown)
    {
        if (shown)
        {
            Go(button, Channel.Opacity, 1, Ms(140), Ease, Ms(120));
            Go(button, Channel.Scale, 1, Ms(140), Out, Ms(120));
            return;
        }
        Go(button, Channel.Opacity, 0, Ms(140), Ease);
        Go(button, Channel.Scale, 0.82, Ms(140), Out);
        look.Reset();
    }

    void MoveSheen()
    {
        var (x, y) = parts.Photo.SheenAt(_nx, _ny);
        Go(parts.Sheen, Channel.X, x, Ms(160), Out);
        Go(parts.Sheen, Channel.Y, y, Ms(160), Out);
    }

    /// <summary>A Slow duration at the Speed setting, or none while nothing eases.</summary>
    double Ms(double slow) => _animate && board is { } b ? b.Clock.Scaled(slow) : 0;

    /// <summary>Eases one channel to <paramref name="target"/>, or puts it there with no time; nothing if it is already on its way there.</summary>
    void Go(Visual visual, Channel channel, double target, double ms, Easing easing, double delay = 0)
    {
        var key = (visual, channel);
        if (_targets.TryGetValue(key, out var last) && last.Target == target && last.Playing is not { Cancelled: true })
        {
            return;
        }
        if (ms > 0 && _animate)
        {
            _targets[key] = (target, keyframer.Transition(visual, channel, target, ms, easing, delay));
            return;
        }
        if (_targets.ContainsKey(key))
        {
            keyframer.Set(visual, channel, target);
        }
        else
        {
            // A new print's first look: nothing runs on it yet.
            Put(visual, channel, target);
        }
        _targets[key] = (target, null);
    }

    /// <summary>Puts a channel at <paramref name="value"/> straight on the visual, for a layer no motion is running on.</summary>
    public static void Put(Visual visual, Channel channel, double value)
    {
        var v = (float)value;
        switch (channel)
        {
            case Channel.Opacity:
                visual.Opacity = v;
                break;
            case Channel.X or Channel.Y:
                visual.Properties.TryGetVector3("Translation", out var at);
                visual.Properties.InsertVector3("Translation", channel == Channel.X ? at with { X = v } : at with { Y = v });
                break;
            case Channel.Scale:
                visual.Scale = new System.Numerics.Vector3(v, v, 1);
                break;
            case Channel.Rotation:
                visual.RotationAngleInDegrees = v;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "a print's looks never scale one axis");
        }
    }
}
