using System.Runtime.InteropServices.Marshalling;
using Tack.Windows.Files;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Tests.Files;

public sealed unsafe class RecycleBinTests
{
    const uint RecycleIfPossible = (uint)_TRANSFER_SOURCE_FLAGS.TSF_DELETE_RECYCLE_IF_POSSIBLE;

    // Explicit: it puts a real file in this machine's Recycle Bin, so it runs
    // only when asked for (dotnet test -- --explicit on).
    [Fact(Explicit = true)]
    public void A_recycled_file_is_gone_from_its_folder()
    {
        using var folder = new Scratch("shell");
        var file = folder.SmallFile("Screenshot 2026-01-02 030405.png");
        var neighbour = folder.SmallFile("neighbour.png");

        new RecycleBin().Recycle(file);

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(neighbour), "only the file named goes");
    }

    [Fact]
    public void A_missing_file_is_an_IOException()
    {
        using var folder = new Scratch("shell");
        Assert.ThrowsAny<IOException>(() => new RecycleBin().Recycle(Path.Combine(folder.Path, "gone.png")));
    }

    [Fact]
    public void A_folder_is_not_a_file_and_stays()
    {
        using var folder = new Scratch("shell");
        var inner = Directory.CreateDirectory(Path.Combine(folder.Path, "inner")).FullName;
        Assert.ThrowsAny<IOException>(() => new RecycleBin().Recycle(inner));
        Assert.True(Directory.Exists(inner));
    }

    [Fact]
    public void A_delete_that_would_recycle_goes_ahead()
    {
        var guard = new RecycleOnly();
        Assert.Equal(0, guard.PreDeleteItem(RecycleIfPossible, 0));
        Assert.False(guard.Refused);
    }

    [Fact]
    public void A_delete_for_good_is_stopped()
    {
        var guard = new RecycleOnly();
        Assert.Equal(HRESULT.E_ABORT.Value, guard.PreDeleteItem(0, 0));
        Assert.True(guard.Refused);
    }

    [Fact]
    public void The_guard_answers_the_shell_through_its_own_v_table()
    {
        // The shell calls the guard through the native interface: this checks
        // the declared methods line up with the shell's slots.
        var guard = new RecycleOnly();
        var sink = (IFileOperationProgressSink*)ComInterfaceMarshaller<IOperationProgressSink>.ConvertToUnmanaged(guard);
        try
        {
            sink->PreDeleteItem(RecycleIfPossible, null);
            Assert.False(guard.Refused);
            var refused = Assert.ThrowsAny<Exception>(() => sink->PreDeleteItem(0, null));
            Assert.Equal(HRESULT.E_ABORT.Value, refused.HResult);
            Assert.True(guard.Refused);
        }
        finally
        {
            ComInterfaceMarshaller<IOperationProgressSink>.Free(sink);
        }
    }
}
