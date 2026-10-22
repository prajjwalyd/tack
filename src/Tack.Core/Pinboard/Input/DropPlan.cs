namespace Tack.Core.Pinboard.Input;

/// <summary>What to do with a drop: its outcome, the pictures to pin, or the text to pin.</summary>
public sealed record DropPlan(DropOutcome Outcome, IReadOnlyList<DroppedFile> Images, string? Text)
{
    public static DropPlan Of(DropOutcome outcome) => new(outcome, [], null);
}
