using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tack.Core.Tests.Storage;

/// <summary>Runs one JSON converter on its own, as board.json's reader and writer use it for one field.</summary>
static class ConverterText
{
    public static T Read<T>(JsonConverter<T> converter, string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return converter.Read(ref reader, typeof(T), JsonSerializerOptions.Default)!;
    }

    public static string Write<T>(JsonConverter<T> converter, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            converter.Write(writer, value, JsonSerializerOptions.Default);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
