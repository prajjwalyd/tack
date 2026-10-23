using Microsoft.Win32;
using Tack.Core;
using Tack.Windows.Startup;

namespace Tack.Windows.Tests.Startup;

/// <summary>
/// Each test works in a throwaway key under HKCU\Software\Tack.Tests, deleted
/// afterwards. The real Run key is never written.
/// </summary>
public sealed class AutostartTests : IDisposable
{
    const string Parent = @"Software\Tack.Tests";
    const string Exe = @"C:\Program Files\Tack Somewhere\Tack.exe";

    readonly string _key = $@"{Parent}\Run-{Guid.NewGuid():N}";

    Autostart At(string exe = Exe) => new("Tack", exe, _key);

    string? Data()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_key);
        return key?.GetValue("Tack") as string;
    }

    [Fact]
    public void Off_until_set()
    {
        Assert.False(At().Enabled);
    }

    [Fact]
    public void On_writes_the_quoted_exe_path()
    {
        var autostart = At();
        autostart.Set(true);

        Assert.True(autostart.Enabled);
        Assert.Equal($"\"{Exe}\"", Data());
        using var key = Registry.CurrentUser.OpenSubKey(_key)!;
        Assert.Equal(RegistryValueKind.String, key.GetValueKind("Tack"));
    }

    [Fact]
    public void Off_removes_the_value_and_leaves_the_others()
    {
        At().Set(true);
        new Autostart("Other app", Exe, _key).Set(true);

        At().Set(false);

        Assert.False(At().Enabled);
        Assert.True(new Autostart("Other app", Exe, _key).Enabled);
        // Turning off what is already off is fine.
        At().Set(false);
    }

    [Fact]
    public void Refreshing_follows_a_moved_exe()
    {
        At(@"C:\Old\Tack.exe").Set(true);

        At().Refresh();

        Assert.Equal($"\"{Exe}\"", Data());
    }

    [Fact]
    public void Refreshing_leaves_a_value_that_already_points_here_unwritten()
    {
        // Tack 0.2's value names tack.exe; the same file, so it stays as it is.
        var older = Exe.Replace("Tack.exe", "tack.exe", StringComparison.Ordinal);
        At(older).Set(true);

        At().Refresh();

        Assert.Equal($"\"{older}\"", Data());
    }

    [Fact]
    public void Refreshing_leaves_autostart_off()
    {
        At().Refresh();

        Assert.False(At().Enabled);
        using var key = Registry.CurrentUser.OpenSubKey(_key);
        Assert.Null(key);
    }

    [Fact]
    public void Without_an_exe_path_nothing_is_written()
    {
        At(exe: "").Set(true);

        Assert.False(At().Enabled);
    }

    [Fact]
    public void Release_and_development_builds_use_their_own_values_in_the_run_key()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", Autostart.RunKey);
        Assert.Equal("Tack", Autostart.For(AppIdentity.Release).ValueName);
        Assert.Equal(AppIdentity.Development.RunValueName, Autostart.For(AppIdentity.Development).ValueName);
        Assert.NotEqual("Tack", AppIdentity.Development.RunValueName);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
        try
        {
            Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false);
        }
        catch (InvalidOperationException)
        {
            // Another test's key is still in it.
        }
    }
}
