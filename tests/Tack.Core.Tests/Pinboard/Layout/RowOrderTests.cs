using Tack.Core.Pinboard.Layout;

namespace Tack.Core.Tests.Pinboard.Layout;

/// <summary>Golden values from Tack 0.2's own reordering, run as it shipped.</summary>
public class RowOrderTests
{
    public static TheoryData<string[], string[], string[]> Orders => new()
    {
        { ["a", "b", "c"], ["c", "a", "b"], ["c", "a", "b"] },
        // An id missing from the new order is about to leave: it keeps its place after its neighbour.
        { ["a", "b", "c"], ["b", "c"], ["a", "b", "c"] },
        { ["a", "b", "c"], ["a", "c"], ["a", "b", "c"] },
        { ["a", "b", "c", "d"], ["d", "c"], ["a", "b", "d", "c"] },
        // Unknown ids and repeats are ignored.
        { ["a", "b", "c"], ["c", "x", "b", "b", "a", "y"], ["c", "b", "a"] },
        { ["a", "b", "c"], [], ["a", "b", "c"] },
        { [], ["a"], [] },
        { ["a", "b", "c", "d", "e"], ["e", "b", "d"], ["a", "e", "b", "c", "d"] },
    };

    [Theory]
    [MemberData(nameof(Orders))]
    public void The_new_order_keeps_known_ids_once_and_missing_ones_in_place(string[] current, string[] ids, string[] expected)
    {
        Assert.Equal(expected, RowOrder.Merge(current, ids, current.Contains));
    }

    [Fact]
    public void The_newest_of_a_backlog_is_by_pinned_time_then_arrival()
    {
        Assert.Equal(-1, RowOrder.Newest([]));
        Assert.Equal(1, RowOrder.Newest([(100, 5), (300, 1), (200, 9)]));
        Assert.Equal(2, RowOrder.Newest([(100, 5), (100, 6), (100, 7)]));
        // The first of equals wins.
        Assert.Equal(0, RowOrder.Newest([(100, 5), (100, 5)]));
    }
}
