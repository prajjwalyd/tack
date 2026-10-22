using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tack.Core.Storage.Json;

/// <summary>
/// Reads a saved choice that may not be one any more. board.json is
/// hand-editable and may come from a newer Tack, so a choice Tack does not
/// know, or a value that is not even a string, reads as the default and never
/// stops the file loading. Written as a plain string.
/// </summary>
public abstract class LenientStringConverter<T> : JsonConverter<T>
{
    /// <summary>A JSON null reaches <see cref="Read"/> too, and reads as the default.</summary>
    public override bool HandleNull => true;

    protected abstract T Fallback { get; }

    protected abstract bool TryParse(string text, [MaybeNullWhen(false)] out T value);

    protected abstract string Format(T value);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && TryParse(reader.GetString()!, out var value))
        {
            return value;
        }
        reader.Skip();
        return Fallback;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Format(value));
}
