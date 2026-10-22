using Tack.Core.Captures;

namespace Tack.Core.Pinboard.Input;

/// <summary>
/// What a drop on the board pins. Files come first: their PNG and JPEG
/// pictures (by MIME type or name) of at most 40 MB are pinned, and files with
/// none get a notice. Without files, the first address of a link list is
/// pinned, else any text that is not blank, as a note. Nothing is ever
/// fetched. The app checks each picture's own bytes again
/// (<see cref="DroppedImage"/>).
/// </summary>
public static class DropRules
{
    static readonly string[] s_imageTypes = ["image/png", "image/jpeg"];
    static readonly string[] s_imageExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>Whether a drag carrying these kinds of data could be pinned: what it carries is only known at the drop.</summary>
    public static bool Pinnable(bool files, bool link, bool text) => files || link || text;

    /// <summary>A PNG or JPEG, by its MIME type or its name's extension.</summary>
    public static bool IsImage(DroppedFile file) =>
        s_imageTypes.Contains(file.ContentType ?? "", StringComparer.OrdinalIgnoreCase)
        || s_imageExtensions.Any(extension => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The plan for a drop of <paramref name="files"/>, a link list
    /// (<paramref name="links"/>, one address a line, # lines are comments)
    /// and <paramref name="text"/>; each may be empty or missing.
    /// </summary>
    public static DropPlan Plan(IReadOnlyList<DroppedFile> files, string? links, string? text)
    {
        if (files.Count > 0)
        {
            var images = files.Where(IsImage).ToList();
            if (images.Count == 0)
            {
                return DropPlan.Of(DropOutcome.NotPinnable);
            }
            var fit = images.Where(file => !DroppedImage.TooLarge(file.Size)).ToList();
            return fit.Count == 0 ? DropPlan.Of(DropOutcome.BadImage) : new DropPlan(DropOutcome.Images, fit, null);
        }
        if (FirstAddress(links) is { } address)
        {
            return new DropPlan(DropOutcome.Text, [], address);
        }
        return string.IsNullOrWhiteSpace(text) ? DropPlan.Of(DropOutcome.Nothing) : new DropPlan(DropOutcome.Text, [], text);
    }

    /// <summary>The first line of a link list that is neither blank nor a # comment, trimmed; null if none.</summary>
    public static string? FirstAddress(string? links) =>
        links?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'));
}
