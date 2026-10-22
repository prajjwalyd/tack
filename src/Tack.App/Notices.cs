using Tack.Core.Diagnostics;

namespace Tack.App;

/// <summary>
/// What the board says, briefly, when something could not be pinned. There is
/// no board yet, so a notice is only traced for now.
/// </summary>
static class Notices
{
    public const string NothingSelected = "Nothing selected";
    public const string AlreadyPinned = "Already pinned";
    public const string Private = "Not pinned: marked private";
    public const string NotPinnable = "Only images and text can be pinned";
    public const string BadImage = "Can't pin that image";
    public const string Failed = "Couldn't pin that";
    public const string NotRestored = "Couldn't restore your clipboard";

    /// <summary>Shows <paramref name="text"/> under the board, which comes down briefly for it.</summary>
    public static void Show(string text) => DevTrace.Write($"notice: {text}; there is no board yet");
}
