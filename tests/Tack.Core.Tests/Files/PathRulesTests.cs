using Tack.Core.Files;

namespace Tack.Core.Tests.Files;

public class PathRulesTests
{
    [Fact]
    public void Path_keys_ignore_case() =>
        Assert.Equal(PathRules.Key(@"C:\Pics\A.PNG"), PathRules.Key(@"c:\pics\a.png"));

    [Fact]
    public void Only_screenshot_formats_are_images()
    {
        Assert.True(PathRules.IsImage("a.PNG"));
        Assert.True(PathRules.IsImage("a.jpeg"));
        Assert.False(PathRules.IsImage("a.gif"));
        Assert.False(PathRules.IsImage("png"));
    }

    [Fact]
    public void Plain_paths_have_no_dots()
    {
        Assert.True(PathRules.IsPlain(@"C:\Tack\Captures\a.png"));
        Assert.False(PathRules.IsPlain(@"C:\Tack\Captures\..\a.png"));
        Assert.False(PathRules.IsPlain(@"..\a.png"));
        Assert.False(PathRules.IsPlain(@"C:\Tack\Captures\.."));
        Assert.False(PathRules.IsPlain(@"C:\"));
    }

    [Fact]
    public void The_same_folder_spelled_another_way_is_the_same()
    {
        using var scratch = new Scratch("same-dir");
        var dir = scratch.Path;
        var trailing = dir + System.IO.Path.DirectorySeparatorChar;
        var resolved = CanonicalPath.Of(dir)!;
        var other = scratch.Folder("sub");

        Assert.True(CanonicalPath.SameDirectory(dir, trailing));
        Assert.True(CanonicalPath.SameDirectory(dir, resolved));
        Assert.False(CanonicalPath.SameDirectory(dir, other));
    }

    [Fact]
    public void A_short_name_and_a_long_name_are_the_same_folder()
    {
        using var scratch = new Scratch("short-name");
        var longDir = scratch.Folder("A folder with a long name");
        var shortDir = System.IO.Path.Combine(scratch.Path, "AFOLDE~1");
        Assert.SkipUnless(Directory.Exists(shortDir), "This volume makes no 8.3 names.");

        Assert.True(CanonicalPath.SameDirectory(longDir, shortDir));
    }
}
