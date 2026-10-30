using Tack.Core.Captures;
using Tack.Core.Files;

namespace Tack.Core.Pinboard.Input;

/// <summary>
/// What a drop on the board pins. Files come first, at most
/// <see cref="MaxItems"/> of them: PNG and JPEG pictures (by MIME type or
/// name) of at most 40 MB are pinned as pictures, and every other file or
/// folder on a drive letter is pinned where it is, never copied. Without
/// files, the first address of a link list is pinned, else any text that is
/// not blank, as a note. Nothing is ever fetched. The app checks each
/// picture's own bytes again (<see cref="DroppedImage"/>).
/// </summary>
public static class DropRules
{
    /// <summary>The most items one drop pins; the rest get a notice, so a whole folder's contents can't flood the board.</summary>
    public const int MaxItems = 20;

    static readonly string[] s_imageTypes = ["image/png", "image/jpeg"];
    static readonly string[] s_imageExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>Whether a drag carrying these kinds of data could be pinned: what it carries is only known at the drop.</summary>
    public static bool Pinnable(bool files, bool link, bool text) => files || link || text;

    /// <summary>A PNG or JPEG, by its MIME type or its name's extension.</summary>
    public static bool IsImage(DroppedFile file) =>
        !file.Folder
        && (s_imageTypes.Contains(file.ContentType ?? "", StringComparer.OrdinalIgnoreCase)
            || s_imageExtensions.Any(extension => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// A file or folder that can be pinned where it is: one with a full path
    /// on a drive letter (<see cref="PathRules.IsOnDrive"/>).
    /// </summary>
    public static bool IsLinkable(DroppedFile file) => file.Path is { } path && PathRules.IsOnDrive(path);

    /// <summary>
    /// The plan for a drop of <paramref name="files"/>, a link list
    /// (<paramref name="links"/>, one address a line, # lines are comments)
    /// and <paramref name="text"/>; each may be empty or missing.
    /// </summary>
    public static DropPlan Plan(IReadOnlyList<DroppedFile> files, string? links, string? text)
    {
        if (files.Count > 0)
        {
            return PlanFiles(files);
        }
        if (FirstAddress(links) is { } address)
        {
            return new DropPlan(DropOutcome.Text, [], [], address);
        }
        return string.IsNullOrWhiteSpace(text) ? DropPlan.Of(DropOutcome.Nothing) : new DropPlan(DropOutcome.Text, [], [], text);
    }

    /// <summary>The first line of a link list that is neither blank nor a # comment, trimmed; null if none.</summary>
    public static string? FirstAddress(string? links) =>
        links?.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'));

    /// <summary>
    /// The first <see cref="MaxItems"/> files: pictures that fit are read as
    /// pictures, as they always were; the rest, a picture too large to read
    /// among them, are linked if they have a place on disk.
    /// </summary>
    static DropPlan PlanFiles(IReadOnlyList<DroppedFile> files)
    {
        var over = Math.Max(0, files.Count - MaxItems);
        var taken = files.Take(MaxItems).ToList();
        var (images, linked) = (new List<DroppedFile>(), new List<DroppedFile>());
        foreach (var file in taken)
        {
            if (IsImage(file) && !DroppedImage.TooLarge(file.Size))
            {
                images.Add(file);
            }
            else if (IsLinkable(file))
            {
                linked.Add(file);
            }
        }
        if (images.Count > 0 || linked.Count > 0)
        {
            return new DropPlan(DropOutcome.Files, images, linked, null, over);
        }
        return DropPlan.Of(taken.Any(IsImage) ? DropOutcome.BadImage : DropOutcome.NotPinnable, over);
    }
}
