using System.Text.Json.Serialization;

namespace Tack.Core.Updates.Feed;

/// <summary>
/// The JSON of latest.json, generated at build time so it works under
/// Native AOT with no reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseFeed))]
internal sealed partial class FeedJsonContext : JsonSerializerContext;
