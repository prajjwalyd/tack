using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Tack.Core.Storage.Json;

/// <summary>
/// How board.json is read and written, to match the files Tack 0.2 wrote byte
/// for byte: pretty JSON with a 2-space indent and \n line breaks, text written
/// as it is rather than escaped, and nothing more forgiving than Tack 0.2 was
/// (a null or a missing required field fails the file).
/// </summary>
internal static class BoardJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = TackJsonContext.Default,
        // These options, not the context's own, name the properties.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        RespectNullableAnnotations = true,
    };

    public static JsonTypeInfo<StoredBoard> Board { get; } = (JsonTypeInfo<StoredBoard>)Options.GetTypeInfo(typeof(StoredBoard));
}
