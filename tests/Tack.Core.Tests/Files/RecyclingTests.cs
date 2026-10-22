using Tack.Core.Files;

namespace Tack.Core.Tests.Files;

public class RecyclingTests
{
    [Fact]
    public void A_missing_file_is_not_sent()
    {
        var bin = new NotingBin();

        Assert.False(Recycling.RecycleFile(@"Z:\no\such\file.png", bin));
        Assert.Empty(bin.Taken);
    }

    [Fact]
    public void A_refused_file_stays_where_it_is()
    {
        using var scratch = new Scratch("recycle");
        var file = scratch.File("capture.png", "x");

        Assert.False(Recycling.RecycleFile(file, new BrokenBin()));
        Assert.True(File.Exists(file), "a failed recycle never deletes the file instead");
    }
}
