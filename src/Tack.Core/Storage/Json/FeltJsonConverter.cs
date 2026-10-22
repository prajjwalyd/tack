using System.Diagnostics.CodeAnalysis;
using Tack.Core.Appearance;

namespace Tack.Core.Storage.Json;

/// <summary>Saves a <see cref="Felt"/> as its text; anything unreadable is the default.</summary>
public sealed class FeltJsonConverter : LenientStringConverter<Felt>
{
    protected override Felt Fallback => Felt.Default;

    protected override bool TryParse(string text, [MaybeNullWhen(false)] out Felt value) =>
        (value = Felt.TryParse(text)) != null;

    protected override string Format(Felt value) => value.ToString();
}
