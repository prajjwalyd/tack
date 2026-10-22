using Tack.Core.Settings;

namespace Tack.Core.Storage;

/// <summary>board.json's "updates".</summary>
internal sealed class StoredUpdates
{
    public bool Check { get; set; } = true;

    public UpdateSettings ToSettings() => new() { Check = Check };

    public static StoredUpdates Of(UpdateSettings settings) => new() { Check = settings.Check };
}
