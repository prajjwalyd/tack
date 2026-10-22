using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tack.App.Pinboard.Automation;
using Tack.App.Pinboard.Motion;
using Tack.Core.Appearance;
using Tack.Core.Pinboard;
using Tack.Core.Pinboard.Motion;
using Tack.Core.Prints;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// One print's layers on the board, outermost first. Each layer is a
/// <see cref="PrintLayer"/>: an element and its compositor visual. Drop, Swing
/// and Card are the slot's size and turn and scale about (50%, 7 px), the
/// pin's centre; so does every motion on them. Each layer has one job, so
/// motions on different layers never fight:
/// <list type="bullet">
/// <item>Slot: placed in the row (the row's slides); holds Drop, the pin and the caption. Takes the pointer.</item>
/// <item>Drop: arrival, the keep press, the reorder lift and the fall.</item>
/// <item>Swing: the pendulum about the pin (nudges, the breeze).</item>
/// <item>Card: the resting tilt, hover lift and lean, press and drag peel (this view's own); holds the shade, paper, badge, buttons and focus ring.</item>
/// <item>Shade: the lift shadow, faded in on hover; scales about its middle.</item>
/// <item>Paper: the paper, its rest shadow and the photo or note on it; Picture is the photo's thumbnail.</item>
/// <item>Badge: the copied check, centred, hidden at rest; scales about its middle.</item>
/// <item>KeepButton and XButton: shown on hover at the top corners (Keep left, unpin right).</item>
/// <item>FocusRing: the keyboard's ring around the card, hidden at rest.</item>
/// <item>Pin: centred at (50%, 7 px), turning and scaling about its own centre; holds PinShadow (its contact shadow, at rest at opacity <see cref="PinShadowOpacity"/>, scaling about its middle), Brass (a kept print's head) and Glint (the band crossing the brass, resting hidden <see cref="GlintTravel"/> to the left).</item>
/// <item>Caption: the age under the print, at its height + 5; its words through <see cref="SetAge"/>.</item>
/// </list>
/// The state looks (hover, press, drag, kept, focus) are this view's; the
/// motions on Drop, Swing, Pin, PinShadow, Glint, Badge and Picture are the
/// print motions'. UI thread only.
/// </summary>
sealed class PrintView
{
    /// <summary>The contact shadow's opacity at rest; the pin-on's dimple ends there.</summary>
    public const double PinShadowOpacity = 0.9;

    /// <summary>The glint rests this far left of the brass, and sweeps to as far right of it (Translation.X).</summary>
    public const double GlintTravel = 0.75 * PinArt.GlintSize;

    const double ButtonInset = BoardMetrics.PrintBorder + 6;

    readonly BoardTheme _theme;
    readonly BoardContext? _board;
    readonly Keyframer _keyframer;
    readonly PrintFace _face = new();
    readonly PinArt _pin = new();
    readonly NineGridShadow _lift = new();
    readonly PrintButton _keep = new(Icons.Keep(), danger: false);
    readonly PrintButton _unpin = new(Icons.Cross(), danger: true);
    readonly CopiedBadge _badge = new();
    readonly KeyboardRing _ring = new();
    readonly PrintCaption _caption = new();
    readonly PeelCard _peel = new();
    readonly PrintStates _states;
    readonly ScaleWatch _scale;
    readonly PrintStack _stack;
    PinColor _pinColor;
    double _artScale;

    /// <param name="theme">The board's colours.</param>
    /// <param name="board">The board, for its keyframer, speed, reduced motion and note paper; without it every look is put in place at once.</param>
    public PrintView(BoardTheme theme, BoardContext? board = null)
    {
        (_theme, _board) = (theme, board);
        Slot = new("slot", new PeerCanvas());
        _scale = new ScaleWatch(Slot.Element, board?.State.Scale ?? 1, _ => ApplyArt());
        _stack = new PrintStack(Slot.Element);
        Drop = new("drop", new Canvas());
        Swing = new("swing", new Canvas());
        Card = new("card", new Canvas());
        Shade = new("shade", _lift.Element);
        Paper = new("paper", _face.Root);
        Picture = new("picture", _face.Picture);
        Badge = new("badge", _badge.Element);
        KeepButton = new("keep", _keep.Element);
        XButton = new("unpin", _unpin.Element);
        FocusRing = new("focus ring", _ring.Element);
        Pin = new("pin", _pin.Root);
        PinShadow = new("pin shadow", _pin.Shadow);
        Brass = new("brass", _pin.Brass);
        Glint = new("glint", _pin.GlintBand);
        Caption = new("caption", _caption.Element);
        _keyframer = board?.Keyframer ?? new Keyframer(Card.Visual.Compositor);
        var parts = new PrintParts(_peel.Visual, _peel.CurlVisual, _face.Photo.Sheen, _pin.Body, _face.Photo, _keep, _unpin);
        _states = new PrintStates(this, parts, board, _keyframer);
        _keep.Pressed += down => _states.ButtonPressed(KeepButton, down);
        _unpin.Pressed += down => _states.ButtonPressed(XButton, down);
        Compose();
        Rest();
        ApplyTheme();
    }

    public PrintLayer Slot { get; }

    public PrintLayer Drop { get; }

    public PrintLayer Swing { get; }

    public PrintLayer Card { get; }

    public PrintLayer Shade { get; }

    public PrintLayer Paper { get; }

    /// <summary>A photo's thumbnail: a refreshed picture flashes in on it (a note flashes its Paper).</summary>
    public PrintLayer Picture { get; }

    public PrintLayer Badge { get; }

    public PrintLayer KeepButton { get; }

    public PrintLayer XButton { get; }

    public PrintLayer FocusRing { get; }

    public PrintLayer Pin { get; }

    public PrintLayer PinShadow { get; }

    public PrintLayer Brass { get; }

    /// <summary>The glint's band inside the brass head, clipped to it.</summary>
    public PrintLayer Glint { get; }

    public PrintLayer Caption { get; }

    public bool Hovered { get; private set; }

    public bool Pressing { get; private set; }

    public bool Dragging { get; private set; }

    public bool Kept { get; private set; }

    public bool Focused { get; private set; }

    /// <summary>Pinning on (raised over hovered prints, under leaving ones).</summary>
    public bool Arriving { get => _stack.Arriving; set => _stack.Arriving = value; }

    /// <summary>Lifted while the row reorders (under a hovered print).</summary>
    public bool Moving { get => _stack.Moving; set => _stack.Moving = value; }

    /// <summary>On its way off the board: over everything.</summary>
    public bool Leaving { get => _stack.Leaving; set => _stack.Leaving = value; }

    /// <summary>Shows the print's picture or note and its kept state.</summary>
    public void Update(Print print)
    {
        _face.Show(print);
        _caption.SetLink(print.Note?.Link);
        SetKept(print.Kept, animate: false);
    }

    /// <summary>Sizes every layer for a print <paramref name="w"/> by <paramref name="h"/>, the paper border included.</summary>
    public void Resize(double w, double h)
    {
        foreach (var layer in new[] { Slot, Drop, Swing, Card })
        {
            (layer.Element.Width, layer.Element.Height) = (w, h);
        }
        _peel.Resize(Card.Visual, w, h);
        _lift.Resize(w, h);
        _face.Resize(w, h);
        _ring.Resize(w, h);
        var origin = new Vector3((float)(w / 2), (float)BoardMetrics.PinY, 0);
        Drop.Visual.CenterPoint = Swing.Visual.CenterPoint = Card.Visual.CenterPoint = origin;
        Shade.Visual.CenterPoint = new Vector3((float)(w / 2), (float)(h / 2), 0);
        Place(Badge, (w - CopiedBadge.Size) / 2, (h - CopiedBadge.Size) / 2);
        Place(KeepButton, ButtonInset, ButtonInset);
        Place(XButton, w - ButtonInset - PrintButton.Size, ButtonInset);
        Place(FocusRing, -KeyboardRing.Outset, -KeyboardRing.Outset);
        var pin = BoardMetrics.PinSize;
        Place(Pin, (w - pin) / 2, BoardMetrics.PinY - pin / 2);
        _caption.Place(w, h);
    }

    /// <summary>The slot's top-left in the row's track, logical px (relative to the board's centre).</summary>
    public void PlaceAt(double x, double y) => Place(Slot, x, y);

    /// <summary>The resting tilt, degrees, on the card; put in place at once.</summary>
    public void SetTilt(double degrees)
    {
        _states.Tilt = degrees;
        _states.Apply(animate: false);
    }

    public void SetPinColor(PinColor color)
    {
        _pinColor = color;
        _pin.Show(color, _theme.IsDark, _artScale);
    }

    /// <summary>Light, dark, high contrast or the note paper changed.</summary>
    public void ApplyTheme()
    {
        _face.ApplyTheme(_theme, NoteTones.Of(_theme));
        _caption.ApplyTheme(_theme);
        _ring.ApplyTheme(_theme);
        ApplyArt();
    }

    /// <summary>The hover look: lifted, buttons and caption shown; un-hovering lets go of the lean.</summary>
    public void SetHover(bool on) => Set(() => Hovered = on);

    /// <summary>
    /// The pointer over the hovered print, <paramref name="nx"/> and
    /// <paramref name="ny"/> each -1 to 1 across its slot: the card leans
    /// toward it and the sheen follows. Nothing under reduced motion.
    /// </summary>
    public void Lean(double nx, double ny) => _states.Lean(nx, ny);

    /// <summary>A press is held on it: it sinks as the hold builds.</summary>
    public void SetPressing(bool on) => Set(() => Pressing = on);

    /// <summary>Its file is being dragged out: it peels off the board, half see-through.</summary>
    public void SetDragging(bool on) => Set(() => Dragging = on);

    /// <summary>Kept: the brass pin and a filled Keep button. <paramref name="animate"/>: fades, only while revealed.</summary>
    public void SetKept(bool kept, bool animate)
    {
        if (kept != Kept)
        {
            Kept = kept;
            _states.Keep(kept, animate);
        }
    }

    /// <summary>The keyboard's ring and selection.</summary>
    public void SetFocused(bool on)
    {
        Focused = on;
        _keyframer.Set(FocusRing.Visual, Channel.Opacity, on ? 1 : 0);
    }

    /// <summary>The age under the print, in words ("2 minutes ago"); shown while hovered.</summary>
    public void SetAge(string text) => _caption.SetAge(text);

    void Set(Action change)
    {
        change();
        _stack.Hovered = Hovered;
        _states.Apply();
    }

    /// <summary>The baked art for the theme and the screen's scale.</summary>
    void ApplyArt()
    {
        _artScale = PrintArt.ScaleFor(_scale.Scale);
        _lift.Show(_theme.IsHighContrast ? null : ShadowArt.Lift, _theme.IsDark, _artScale);
        _face.Rescale(_scale.Scale, _artScale);
        _keep.ApplyTheme(_theme, _artScale);
        _unpin.ApplyTheme(_theme, _artScale);
        _badge.ApplyTheme(_theme, _artScale);
        SetPinColor(_pinColor);
    }

    void Compose()
    {
        ((Canvas)Slot.Element).Children.Add(Drop.Element);
        ((Canvas)Slot.Element).Children.Add(Pin.Element);
        ((Canvas)Slot.Element).Children.Add(Caption.Element);
        ((Canvas)Drop.Element).Children.Add(Swing.Element);
        ((Canvas)Swing.Element).Children.Add(Card.Element);
        ((Canvas)Card.Element).Children.Add(_peel.Element);
        foreach (var element in new UIElement[] { Shade.Element, Paper.Element, _peel.Curl, Badge.Element, KeepButton.Element, XButton.Element, FocusRing.Element })
        {
            _peel.Element.Children.Add(element);
        }
    }

    /// <summary>Everything that is hidden at rest, hidden; the pin's shadow at its resting depth.</summary>
    void Rest()
    {
        foreach (var hidden in new[] { Shade, Badge, KeepButton, XButton, FocusRing, Brass, Glint, Caption })
        {
            PrintStates.Put(hidden.Visual, Channel.Opacity, 0);
        }
        PrintStates.Put(PinShadow.Visual, Channel.Opacity, PinShadowOpacity);
        PrintStates.Put(Glint.Visual, Channel.X, -GlintTravel);
        PinShadow.Visual.CenterPoint = PinArt.ShadowCentre;
        Badge.Visual.CenterPoint = new Vector3((float)(CopiedBadge.Size / 2), (float)(CopiedBadge.Size / 2), 0);
        KeepButton.Visual.CenterPoint = XButton.Visual.CenterPoint = new Vector3((float)(PrintButton.Size / 2), (float)(PrintButton.Size / 2), 0);
        var pin = (float)(BoardMetrics.PinSize / 2);
        Pin.Visual.CenterPoint = new Vector3(pin, pin, 0);
        _states.Apply(animate: false);
    }

    static void Place(PrintLayer layer, double x, double y)
    {
        Canvas.SetLeft(layer.Element, x);
        Canvas.SetTop(layer.Element, y);
    }
}
