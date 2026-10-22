using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// How the phone board's JSON is written: compact, camelCase field names,
/// nulls kept where the page expects them, and text as it is rather than
/// escaped, as Tack 0.2 wrote it.
/// </summary>
internal static class PhoneJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = PhoneJsonContext.Default,
        // These options, not the context's own, name the properties.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly JsonTypeInfo<BoardView> Board = (JsonTypeInfo<BoardView>)Options.GetTypeInfo(typeof(BoardView));

    static readonly JsonTypeInfo<GateView> Gate = (JsonTypeInfo<GateView>)Options.GetTypeInfo(typeof(GateView));

    public static byte[] Of(BoardView board) => JsonSerializer.SerializeToUtf8Bytes(board, Board);

    public static byte[] Of(GateView gate) => JsonSerializer.SerializeToUtf8Bytes(gate, Gate);
}
