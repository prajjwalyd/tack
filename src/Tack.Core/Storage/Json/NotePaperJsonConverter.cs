using System.Diagnostics.CodeAnalysis;
using Tack.Core.Appearance;

namespace Tack.Core.Storage.Json;

/// <summary>Saves a <see cref="NotePaper"/> as its id; anything unreadable is the default.</summary>
public sealed class NotePaperJsonConverter : LenientStringConverter<NotePaper>
{
    protected override NotePaper Fallback => NotePaper.Default;

    protected override bool TryParse(string text, [MaybeNullWhen(false)] out NotePaper value) =>
        (value = NotePaper.TryParse(text)) != null;

    protected override string Format(NotePaper value) => value.Id;
}
