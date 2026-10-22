using Tack.Core.Reveal;

namespace Tack.Core.Storage.Json;

/// <summary>Saves a <see cref="RevealSpeed"/> as a lowercase word; anything unreadable is Normal.</summary>
public sealed class RevealSpeedJsonConverter : LenientStringConverter<RevealSpeed>
{
    protected override RevealSpeed Fallback => RevealSpeed.Normal;

    protected override bool TryParse(string text, out RevealSpeed value)
    {
        var parsed = RevealSpeedTimes.Parse(text);
        value = parsed ?? RevealSpeed.Normal;
        return parsed.HasValue;
    }

    protected override string Format(RevealSpeed value) => value.Id();
}
