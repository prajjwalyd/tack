using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Tack.App.Pinboard.Prints;

/// <summary>
/// One of a print's small round buttons (Keep, unpin): a pale disc with a
/// hairline rim and a soft shadow, its icon in dark ink. It brightens under
/// the pointer (the cross turns red); Keep shows a kept print with its
/// bookmark filled in brass ink. High contrast draws it as a system button.
/// What a click does is the gestures' business; this is only its look.
/// </summary>
sealed class PrintButton
{
    public const double Size = 18;
    const double RimMargin = 12;

    readonly ArtSprite _rim = new(Size + 2 * RimMargin, Size + 2 * RimMargin);
    readonly Border _disc = new() { Width = Size, Height = Size, CornerRadius = new CornerRadius(Size / 2) };
    readonly Path _icon;
    readonly bool _danger;
    BoardTheme? _theme;
    bool _over;
    bool _kept;

    /// <param name="icon">Its icon on an 18 px grid.</param>
    /// <param name="danger">The pointer turns its ink red (unpin).</param>
    public PrintButton(Path icon, bool danger)
    {
        (_icon, _danger) = (icon, danger);
        Canvas.SetLeft(_rim.Element, -RimMargin);
        Canvas.SetTop(_rim.Element, -RimMargin);
        Element = new Canvas { Width = Size, Height = Size, IsHitTestVisible = false, Children = { _rim.Element, _disc, _icon } };
        Element.PointerEntered += (_, _) => Hover(true);
        Element.PointerExited += (_, _) => Hover(false);
        Element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Press(true)), true);
        Element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Press(false)), true);
        Element.PointerCaptureLost += (_, _) => Press(false);
        Element.PointerCanceled += (_, _) => Press(false);
    }

    /// <summary>The button; it takes the pointer only while shown.</summary>
    public Canvas Element { get; }

    /// <summary>Pressed (true) or let go (false): it shrinks a little while held.</summary>
    public event Action<bool>? Pressed;

    /// <summary>A kept print's Keep button: brass ink, the bookmark filled.</summary>
    public void SetKept(bool kept)
    {
        _kept = kept;
        Paint();
    }

    /// <summary>Hidden: no longer under the pointer.</summary>
    public void Reset()
    {
        _over = false;
        Paint();
    }

    public void ApplyTheme(BoardTheme theme, double artScale)
    {
        _theme = theme;
        _rim.Show(theme.IsHighContrast ? null : "button", artScale);
        Paint();
    }

    void Hover(bool over)
    {
        _over = over;
        Paint();
    }

    void Press(bool down) => Pressed?.Invoke(down);

    void Paint()
    {
        if (_theme is not { } theme)
        {
            return;
        }
        if (theme.IsHighContrast)
        {
            _disc.Background = theme.Brush(_over ? theme.Highlight : theme.ButtonFace);
            _disc.BorderBrush = theme.Brush(theme.ButtonText);
            _disc.BorderThickness = new Thickness(1);
            _icon.Stroke = theme.Brush(_over ? theme.HighlightText : theme.ButtonText);
        }
        else
        {
            var disc = _over
                ? BoardTheme.Rgba(255, 255, 255, 0.88)
                : theme.IsDark ? BoardTheme.Rgba(250, 248, 244, 0.58) : BoardTheme.Rgba(255, 255, 255, 0.66);
            _disc.Background = theme.Brush(disc);
            _disc.BorderThickness = new Thickness(0);
            var ink = _kept ? BoardTheme.Rgb(0x8a, 0x64, 0x20)
                : _danger && _over ? BoardTheme.Rgb(0xa8, 0x36, 0x2a)
                : BoardTheme.Rgb(0x2b, 0x26, 0x20);
            _icon.Stroke = theme.Brush(ink);
        }
        _icon.Fill = _kept ? _icon.Stroke : null;
    }
}
