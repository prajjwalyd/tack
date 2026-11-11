using Tack.Core.Pinboard.Words;

namespace Tack.Core.Tests.Pinboard.Words;

/// <summary>Names cut to a width, measured here as one unit a character.</summary>
public class FileNameFitTests
{
    static string Fit(string name, double max) => FileNameFit.Fit(name, max, text => text.Length);

    [Theory]
    [InlineData("notes.txt", 20)]
    [InlineData("notes.txt", 9)]
    public void A_name_that_fits_stays_whole(string name, double max) => Assert.Equal(name, Fit(name, max));

    [Fact]
    public void A_long_name_keeps_its_start_and_its_extension()
    {
        var fit = Fit("usageReport_1_38d97bc5b3dc4fc2a921843a49747c31.csv", 24);
        Assert.Equal("usageReport_1_38…c31.csv", fit);
        Assert.Equal(24, fit.Length);
    }

    [Fact]
    public void The_widest_start_that_fits_is_kept()
    {
        Assert.Equal("Tack_0.3.0_x…tup.exe", Fit("Tack_0.3.0_x64-setup.exe", 20));
    }

    [Fact]
    public void Spaces_before_the_ellipsis_go()
    {
        Assert.Equal("Q3 review…v3).pdf", Fit("Q3 review final numbers (v3).pdf", 18));
    }

    [Theory]
    [InlineData("Screenshots from the launch week", 12, "Screenshots…")]
    [InlineData("v1.2 final draft of the plan", 10, "v1.2 fina…")]
    [InlineData(".gitignore-and-more-things", 8, ".gitign…")]
    public void A_name_without_an_extension_loses_its_end(string name, double max, string expected) =>
        Assert.Equal(expected, Fit(name, max));

    [Fact]
    public void When_only_the_end_fits_the_start_goes_entirely()
    {
        Assert.Equal("…c31.csv", Fit("usageReport_1_38d97bc5b3dc4fc2a921843a49747c31.csv", 5));
    }

    [Fact]
    public void No_width_yet_leaves_the_name_alone()
    {
        Assert.Equal("usageReport.csv", Fit("usageReport.csv", 0));
    }

    [Fact]
    public void A_character_outside_the_basic_plane_is_never_split()
    {
        Assert.Equal("ab…hij.png", Fit("ab😀cdefghij.png", 11));
    }
}
