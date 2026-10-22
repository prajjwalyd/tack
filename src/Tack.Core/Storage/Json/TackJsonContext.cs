using System.Text.Json.Serialization;

namespace Tack.Core.Storage.Json;

/// <summary>
/// The JSON Tack reads and writes, generated at build time so it works under
/// Native AOT with no reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StoredBoard))]
internal sealed partial class TackJsonContext : JsonSerializerContext;
