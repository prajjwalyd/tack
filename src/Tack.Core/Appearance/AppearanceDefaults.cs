using Tack.Core.Settings;

namespace Tack.Core.Appearance;

/// <summary>What Reset appearance goes back to: the first of each list, the board colour, the frame and the note paper.</summary>
public static class AppearanceDefaults
{
    /// <summary>True when the board colour, the frame and the note paper are all their defaults, so there is nothing to reset.</summary>
    public static bool IsDefault(AppearanceSettings appearance) => appearance == new AppearanceSettings();
}
