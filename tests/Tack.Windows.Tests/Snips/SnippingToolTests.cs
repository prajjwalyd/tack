using Tack.Windows.Snips;

namespace Tack.Windows.Tests.Snips;

public sealed class SnippingToolTests
{
    static readonly string[] s_folders = [@"c:\windows\system32\", @"c:\program files\windowsapps\"];

    [Fact]
    public void Snipping_tool_is_trusted_only_where_windows_installs_it()
    {
        Assert.True(TrustedSnipper.IsTrusted(@"C:\Windows\System32\SnippingTool.exe", s_folders));
        Assert.True(TrustedSnipper.IsTrusted(
            @"C:\Program Files\WindowsApps\Microsoft.ScreenSketch_11.2409.25.0_x64__8wekyb3d8bbwe\SnippingTool\ScreenClippingHost.exe",
            s_folders));
        Assert.False(TrustedSnipper.IsTrusted(@"C:\Users\me\Downloads\SnippingTool.exe", s_folders));
        Assert.False(TrustedSnipper.IsTrusted(@"C:\Windows\System32\..\Temp\SnippingTool.exe", s_folders));
        Assert.False(TrustedSnipper.IsTrusted(@"C:\Windows\System32\notepad.exe", s_folders));
        Assert.False(TrustedSnipper.IsTrusted(@"C:\Windows\System32Evil\SnippingTool.exe", s_folders));
    }

    [Fact]
    public void The_trusted_folders_are_windows_own()
    {
        var folders = TrustedSnipper.Folders;
        Assert.Equal(3, folders.Count);
        Assert.All(folders, folder => Assert.EndsWith(@"\", folder));
        Assert.All(folders, folder => Assert.Equal(folder.ToLowerInvariant(), folder));
        Assert.EndsWith(@"\system32\", folders[0]);
        Assert.EndsWith(@"\systemapps\", folders[1]);
        Assert.EndsWith(@"\windowsapps\", folders[2]);
    }

    [Fact]
    public void Only_a_picture_from_snipping_tool_outside_a_grab_is_read()
    {
        Assert.Null(SnipJudge.Ignored(grabbing: false, fromSnippingTool: true, hasPicture: true));
        Assert.Equal("ignored (pinning the selection)", SnipJudge.Ignored(true, true, true));
        Assert.Equal("ignored", SnipJudge.Ignored(false, false, true));
        // Snipping Tool also copies text, from its text actions.
        Assert.Equal("ignored (no image)", SnipJudge.Ignored(false, true, false));
    }
}
