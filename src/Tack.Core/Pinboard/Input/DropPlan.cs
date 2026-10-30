namespace Tack.Core.Pinboard.Input;

/// <summary>What to do with a drop.</summary>
/// <param name="Outcome">What the drop comes to.</param>
/// <param name="Images">The pictures to read and pin as pictures.</param>
/// <param name="Links">The files and folders to pin where they are.</param>
/// <param name="Text">The text or link to pin as a note.</param>
/// <param name="Over">How many items past <see cref="DropRules.MaxItems"/> were left.</param>
public sealed record DropPlan(DropOutcome Outcome, IReadOnlyList<DroppedFile> Images, IReadOnlyList<DroppedFile> Links, string? Text, int Over = 0)
{
    public static DropPlan Of(DropOutcome outcome, int over = 0) => new(outcome, [], [], null, over);
}
