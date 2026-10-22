using Tack.Windows.Shell;

namespace Tack.Windows.Tests.Shell;

public sealed class ExplorerTests
{
    [Fact]
    public void Explorer_is_found_by_its_full_path_in_the_Windows_folder()
    {
        var exe = Explorer.Exe;
        Assert.NotNull(exe);
        Assert.True(Path.IsPathFullyQualified(exe));
        Assert.Equal("explorer.exe", Path.GetFileName(exe), ignoreCase: true);
        Assert.True(File.Exists(exe));
    }

    [Fact]
    public void The_file_to_select_is_quoted_after_select() =>
        Assert.Equal(
            @"/select,""C:\Users\me\My Pictures\Screenshot 2026-01-02 030405.png""",
            Explorer.SelectArguments(@"C:\Users\me\My Pictures\Screenshot 2026-01-02 030405.png"));
}
