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

    [Fact]
    public void The_installer_is_asked_for_a_passive_update_that_restarts_tack() =>
        Assert.Equal("/P /UPDATE /R", InstallerLauncher.Arguments);
}
