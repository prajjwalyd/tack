namespace Tack.Windows.Snips;

/// <summary>Decides on the spot whether a clipboard change is a capture to read.</summary>
static class SnipJudge
{
    /// <summary>Why the change is ignored, for the development trace; null when it is to be read.</summary>
    public static string? Ignored(bool grabbing, bool fromSnippingTool, bool hasPicture)
    {
        if (grabbing)
        {
            // Pinning the selection copies and restores the clipboard itself.
            return "ignored (pinning the selection)";
        }
        if (!fromSnippingTool)
        {
            return "ignored";
        }
        // Snipping Tool also copies text, from its text actions.
        return hasPicture ? null : "ignored (no image)";
    }
}
