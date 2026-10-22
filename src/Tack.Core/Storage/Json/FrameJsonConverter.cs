using System.Diagnostics.CodeAnalysis;
using Tack.Core.Appearance;

namespace Tack.Core.Storage.Json;

/// <summary>Saves a <see cref="Frame"/> as its id; anything unreadable is the default.</summary>
public sealed class FrameJsonConverter : LenientStringConverter<Frame>
{
    protected override Frame Fallback => Frame.Default;

    protected override bool TryParse(string text, [MaybeNullWhen(false)] out Frame value) =>
        (value = Frame.TryParse(text)) != null;

    protected override string Format(Frame value) => value.Id;
}
