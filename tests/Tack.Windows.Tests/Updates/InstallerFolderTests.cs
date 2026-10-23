using Tack.Windows.Updates;

namespace Tack.Windows.Tests.Updates;

public class InstallerFolderTests
{
    [Fact]
    public void Each_download_gets_a_fresh_folder_of_its_own_under_temp()
    {
        var (one, two) = (InstallerFolder.Fresh(), InstallerFolder.Fresh());
        try
        {
            Assert.NotEqual(one, two);
            Assert.True(Directory.Exists(one) && !Directory.EnumerateFileSystemEntries(one).Any());
            Assert.True(InstallerFolder.Ours(one));
        }
        finally
        {
            Directory.Delete(one);
            Directory.Delete(two);
        }
    }

    [Fact]
    public void Discarding_a_download_deletes_its_folder()
    {
        var file = Path.Combine(InstallerFolder.Fresh(), "Tack-setup.exe");
        File.WriteAllText(file, "not an installer");

        InstallerFolder.Discard(file);

        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }

    [Fact]
    public void A_folder_tack_did_not_make_is_never_deleted()
    {
        var temp = Path.GetTempPath();
        Assert.False(InstallerFolder.Ours(Path.Combine(temp, "something-else")));
        Assert.False(InstallerFolder.Ours(temp));
        Assert.False(InstallerFolder.Ours(Path.Combine(temp, "nested", InstallerFolder.Prefix + "x")));
        Assert.False(InstallerFolder.Ours(Path.Combine(Path.GetPathRoot(temp)!, InstallerFolder.Prefix + "x")));

        var folder = Directory.CreateDirectory(Path.Combine(temp, "tack-test-" + Guid.NewGuid().ToString("N"))).FullName;
        var file = Path.Combine(folder, "keep.exe");
        File.WriteAllText(file, "keep");
        try
        {
            InstallerFolder.Discard(file);
            Assert.True(File.Exists(file));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(23, false)]
    [InlineData(24, false)]
    [InlineData(25, true)]
    [InlineData(24 * 30, true)]
    public void A_download_is_cleared_once_it_is_over_a_day_old(int hoursOld, bool cleared)
    {
        var now = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(cleared, InstallerFolder.IsOld(now.AddHours(-hoursOld), now));
    }

    [Fact]
    public void Clearing_deletes_old_downloads_and_keeps_new_ones_and_other_folders()
    {
        var now = DateTime.UtcNow;
        var old = InstallerFolder.Fresh();
        File.WriteAllText(Path.Combine(old, "Tack-setup.exe"), "ran yesterday");
        Directory.SetLastWriteTimeUtc(old, now.AddDays(-2));
        var recent = InstallerFolder.Fresh();
        var other = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tack-test-" + Guid.NewGuid().ToString("N"))).FullName;
        Directory.SetLastWriteTimeUtc(other, now.AddDays(-2));
        try
        {
            InstallerFolder.ClearOld(now);

            Assert.False(Directory.Exists(old));
            Assert.True(Directory.Exists(recent));
            Assert.True(Directory.Exists(other));
        }
        finally
        {
            foreach (var folder in new[] { old, recent, other }.Where(Directory.Exists))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void The_installer_is_asked_for_a_passive_update_that_restarts_tack() =>
        Assert.Equal("/P /UPDATE /R", InstallerLauncher.Arguments);
}
