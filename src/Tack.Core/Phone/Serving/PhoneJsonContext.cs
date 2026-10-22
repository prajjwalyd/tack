using System.Text.Json.Serialization;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// The JSON the phone board sends, generated at build time so it works under
/// Native AOT with no reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BoardView))]
[JsonSerializable(typeof(GateView))]
internal sealed partial class PhoneJsonContext : JsonSerializerContext;
