using System.Text.Json.Serialization;
using Tack.Core.Appearance;
using Tack.Core.Settings;
using Tack.Core.Storage.Json;

namespace Tack.Core.Storage;

/// <summary>board.json's "appearance": each choice reads as its default on its own when it is not one Tack knows.</summary>
internal sealed class StoredAppearance
{
    [JsonConverter(typeof(FeltJsonConverter))]
    public Felt Felt { get; set; } = Felt.Default;

    [JsonConverter(typeof(FrameJsonConverter))]
    public Frame Frame { get; set; } = Frame.Default;

    [JsonConverter(typeof(NotePaperJsonConverter))]
    public NotePaper Paper { get; set; } = NotePaper.Default;

    public AppearanceSettings ToSettings() => new() { Felt = Felt, Frame = Frame, Paper = Paper };

    public static StoredAppearance Of(AppearanceSettings settings) =>
        new() { Felt = settings.Felt, Frame = settings.Frame, Paper = settings.Paper };
}
