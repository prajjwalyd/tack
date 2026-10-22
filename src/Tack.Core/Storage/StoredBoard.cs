using System.Text.Json.Serialization;
using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Core.Shortcuts;
using Tack.Core.Storage.Json;

namespace Tack.Core.Storage;

/// <summary>
/// board.json's shape, field for field and in this order: the schema version,
/// the prints, then the settings flattened in beside them, as Tack 0.2 wrote it.
/// A missing field keeps the default given here.
/// </summary>
internal sealed class StoredBoard
{
    /// <summary>The schema this build writes and fully understands. 3: notes.</summary>
    public const uint CurrentVersion = 3;

    public uint? Version { get; set; }

    /// <summary>Every Tack writes a list, even an empty one; a file without one is never trusted.</summary>
    public List<StoredPrint>? Prints { get; set; }

    public bool Sound { get; set; } = true;

    public bool EdgeReveal { get; set; } = true;

    [JsonConverter(typeof(RevealSpeedJsonConverter))]
    public RevealSpeed Reveal { get; set; } = RevealSpeed.Normal;

    public StoredAppearance Appearance { get; set; } = new();

    public StoredUpdates Updates { get; set; } = new();

    public bool SnipTipShown { get; set; }

    public string ToggleShortcut { get; set; } = Chord.DefaultToggle;

    public string PinShortcut { get; set; } = Chord.DefaultPin;

    public StoredPhone Phone { get; set; } = new();

    public TackSettings ToSettings() => new()
    {
        Sound = Sound,
        EdgeReveal = EdgeReveal,
        Reveal = Reveal,
        Appearance = Appearance.ToSettings(),
        Updates = Updates.ToSettings(),
        SnipTipShown = SnipTipShown,
        ToggleShortcut = ToggleShortcut,
        PinShortcut = PinShortcut,
        Phone = Phone.ToSettings(),
    };

    public static StoredBoard Of(IReadOnlyList<StoredPrint> prints, TackSettings settings) => new()
    {
        Version = CurrentVersion,
        Prints = [.. prints],
        Sound = settings.Sound,
        EdgeReveal = settings.EdgeReveal,
        Reveal = settings.Reveal,
        Appearance = StoredAppearance.Of(settings.Appearance),
        Updates = StoredUpdates.Of(settings.Updates),
        SnipTipShown = settings.SnipTipShown,
        ToggleShortcut = settings.ToggleShortcut,
        PinShortcut = settings.PinShortcut,
        Phone = StoredPhone.Of(settings.Phone),
    };
}
