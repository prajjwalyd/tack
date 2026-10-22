using Tack.Windows.Startup;

namespace Tack.Windows.Tests.Startup;

public sealed class PackageContainerTests
{
    static readonly Func<int, string?> OnlyTerminal = pid => pid == 30 ? "Terminal_1.0_x64__abc" : null;

    [Fact]
    public void The_nearest_packaged_ancestor_is_the_host()
    {
        // Tack 40, started by a shell 35, under a terminal 30, under Explorer 20.
        var parents = new Dictionary<int, int> { [40] = 35, [35] = 30, [30] = 20, [20] = 0 };
        Assert.Equal("Terminal_1.0_x64__abc", PackageContainer.NearestPackaged(40, parents, OnlyTerminal));
    }

    [Fact]
    public void No_packaged_ancestor_is_no_host()
    {
        var parents = new Dictionary<int, int> { [40] = 35, [35] = 20, [20] = 0 };
        Assert.Null(PackageContainer.NearestPackaged(40, parents, OnlyTerminal));
    }

    [Fact]
    public void An_unknown_parent_ends_the_walk()
    {
        var parents = new Dictionary<int, int> { [40] = 35 };
        Assert.Null(PackageContainer.NearestPackaged(40, parents, OnlyTerminal));
    }

    [Fact]
    public void A_cycle_of_reused_ids_ends_the_walk()
    {
        var parents = new Dictionary<int, int> { [40] = 40 };
        Assert.Null(PackageContainer.NearestPackaged(40, parents, _ => "never asked"));
        var loop = new Dictionary<int, int> { [40] = 35, [35] = 40 };
        Assert.Null(PackageContainer.NearestPackaged(40, loop, OnlyTerminal));
    }

    [Fact]
    public void The_walk_stops_after_sixteen_ancestors()
    {
        var parents = Enumerable.Range(1, 40).ToDictionary(pid => pid, pid => pid + 1);
        Assert.Equal("17", PackageContainer.NearestPackaged(1, parents, pid => pid == 17 ? "17" : null));
        Assert.Null(PackageContainer.NearestPackaged(1, parents, pid => pid == 18 ? "18" : null));
    }

    [Fact]
    public void A_relaunched_or_staying_Tack_runs_where_it_is()
    {
        Assert.True(PackageContainer.MayRelaunch(_ => null));
        Assert.False(PackageContainer.MayRelaunch(name => name == "TACK_RELAUNCHED_FROM_PACKAGE" ? "1" : null));
        Assert.False(PackageContainer.MayRelaunch(name => name == "TACK_STAY_IN_PACKAGE" ? "1" : null));
    }

    [Fact]
    public void The_snapshot_knows_this_process()
    {
        var parents = ProcessParents.Snapshot();
        Assert.True(parents.ContainsKey(Environment.ProcessId));
    }

    [Fact]
    public void A_process_that_cannot_be_opened_has_no_package() =>
        // Process ids are multiples of four, so this one never exists.
        Assert.Null(PackageNames.Of(3));
}
