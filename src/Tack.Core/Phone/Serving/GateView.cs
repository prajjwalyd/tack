using System.Text.Json.Serialization;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// Where a device stands with the gate, as the page reads it:
/// <c>{"state":"allowed"}</c> after asking again, or what the page shows
/// instead of the board ("waiting" with the code, "busy" or "denied", each
/// with both names). Fields that do not apply are left out.
/// </summary>
internal sealed record GateView(
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Pc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? You)
{
    public static GateView Allowed { get; } = new("allowed", null, null, null);

    /// <summary>What a device that is not let in is told.</summary>
    public static GateView Refusal(Verdict verdict, string pc, string you) => verdict switch
    {
        Verdict.Waiting waiting => new("waiting", waiting.Code, pc, you),
        Verdict.Busy => new("busy", null, pc, you),
        _ => new("denied", null, pc, you),
    };
}
