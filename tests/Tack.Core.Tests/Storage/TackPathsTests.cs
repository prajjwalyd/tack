using Tack.Core.Storage;

namespace Tack.Core.Tests.Storage;

public class TackPathsTests
{
    static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;

    static readonly Func<string, string?> Windows = Environment(
        ("APPDATA", @"C:\Users\me\AppData\Roaming"),
        ("LOCALAPPDATA", @"C:\Users\me\AppData\Local"));

    [Fact]
    public void A_release_build_uses_the_folders_tack_0_2_used()
    {
        var paths = TackPaths.Resolve(Windows, development: false);

        Assert.Equal(@"C:\Users\me\AppData\Roaming\Tack\board.json", paths.BoardFile);
        Assert.Equal(@"C:\Users\me\AppData\Local\Tack\Captures", paths.Captures);
        Assert.Equal(@"C:\Users\me\AppData\Local\Tack\Notes", paths.Notes);
    }

    [Fact]
    public void A_development_build_keeps_to_a_scratch_folder_of_its_own()
    {
        var paths = TackPaths.Resolve(Windows, development: true);

        Assert.Equal(@"C:\Users\me\AppData\Local\Tack.Dev\Roaming\board.json", paths.BoardFile);
        Assert.Equal(@"C:\Users\me\AppData\Local\Tack.Dev\Local\Captures", paths.Captures);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tack_home_wins_over_everything(bool development)
    {
        var environment = Environment(
            ("APPDATA", @"C:\Users\me\AppData\Roaming"),
            ("LOCALAPPDATA", @"C:\Users\me\AppData\Local"),
            (TackPaths.HomeVariable, @"D:\scratch"));

        var paths = TackPaths.Resolve(environment, development);

        Assert.Equal(@"D:\scratch\Roaming\board.json", paths.BoardFile);
        Assert.Equal(@"D:\scratch\Local\Notes", paths.Notes);
    }

    [Fact]
    public void A_blank_tack_home_is_not_set()
    {
        var environment = Environment(("APPDATA", @"C:\R"), ("LOCALAPPDATA", @"C:\L"), (TackPaths.HomeVariable, "  "));

        Assert.Equal(@"C:\R\Tack", TackPaths.Resolve(environment, development: false).Roaming);
    }

    [Fact]
    public void Without_app_data_folders_tack_falls_back_to_the_current_folder()
    {
        var paths = TackPaths.Resolve(Environment(), development: false);

        Assert.Equal(@".\Tack\board.json", paths.BoardFile);
        Assert.Equal(@".\Tack\Captures", paths.Captures);
    }
}
