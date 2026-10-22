using System.Runtime.InteropServices.Marshalling;
using Tack.Core.Files;
using Tack.Windows.Shell;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Files;

/// <summary>
/// The Windows Recycle Bin, through the shell's own file operation: undoable
/// from Explorer, with no window, progress or question shown. A file the
/// shell would delete for good instead is refused and stays where it is.
/// Blocks until the shell is done, so call it off the UI thread; any thread
/// will do.
/// </summary>
public sealed unsafe class RecycleBin : IRecycler
{
    const FILEOPERATION_FLAGS Flags = FILEOPERATION_FLAGS.FOF_NO_UI
        | FILEOPERATION_FLAGS.FOF_ALLOWUNDO
        | FILEOPERATION_FLAGS.FOF_WANTNUKEWARNING
        | FILEOPERATION_FLAGS.FOFX_RECYCLEONDELETE;

    public void Recycle(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("There is no such file to recycle.", full);
        }
        IOException? failure = null;
        // The shell's file operation wants a single-threaded apartment. A
        // thread of its own always starts clean, whatever the caller's chose.
        var thread = new Thread(() => failure = Send(full)) { IsBackground = true, Name = "tack-recycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        {
            throw failure;
        }
        if (File.Exists(full))
        {
            throw new IOException($"The shell left {Path.GetFileName(full)} where it was.");
        }
    }

    /// <summary>Recycles the file, or says why not. Runs on the recycling thread.</summary>
    static IOException? Send(string path)
    {
        var guard = new RecycleOnly();
        try
        {
            Perform(path, guard);
            return null;
        }
        catch (Exception e)
        {
            var name = Path.GetFileName(path);
            return guard.Refused
                ? new IOException($"{name} would have been deleted for good, not recycled, so it was kept.", e)
                : e as IOException ?? new IOException($"Cannot recycle {name}: {e.Message}", e);
        }
    }

    static void Perform(string path, RecycleOnly guard)
    {
        FileOperation.CreateInstance(out IFileOperation* operation).ThrowOnFailure();
        IShellItem* item = null;
        var sink = ComInterfaceMarshaller<IOperationProgressSink>.ConvertToUnmanaged(guard);
        try
        {
            operation->SetOperationFlags(Flags);
            item = ShellItem.FromPath(path);
            operation->DeleteItem(item, (IFileOperationProgressSink*)sink);
            operation->PerformOperations();
            BOOL aborted;
            operation->GetAnyOperationsAborted(&aborted);
            if (guard.Refused || aborted)
            {
                throw new IOException("The shell stopped before recycling the file.");
            }
        }
        finally
        {
            if (item != null)
            {
                item->Release();
            }
            ComInterfaceMarshaller<IOperationProgressSink>.Free(sink);
            operation->Release();
        }
    }
}
