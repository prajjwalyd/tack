using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Tack.Windows.Files;

/// <summary>
/// Watches a shell delete and stops it before any file would be destroyed
/// rather than recycled: when the drive has no Recycle Bin, the file is too
/// big for it, or the user turned it off. Tack never deletes a file for good.
/// </summary>
[GeneratedComClass]
internal sealed partial class RecycleOnly : IOperationProgressSink
{
    const int Ok = 0;

    /// <summary>A file was about to be deleted for good, and was kept.</summary>
    public bool Refused { get; private set; }

    public int PreDeleteItem(uint flags, nint item)
    {
        if ((flags & (uint)_TRANSFER_SOURCE_FLAGS.TSF_DELETE_RECYCLE_IF_POSSIBLE) != 0)
        {
            return Ok;
        }
        Refused = true;
        return HRESULT.E_ABORT.Value;
    }

    public int StartOperations() => Ok;

    public int FinishOperations(int result) => Ok;

    public int PreRenameItem(uint flags, nint item, nint newName) => Ok;

    public int PostRenameItem(uint flags, nint item, nint newName, int result, nint created) => Ok;

    public int PreMoveItem(uint flags, nint item, nint destination, nint newName) => Ok;

    public int PostMoveItem(uint flags, nint item, nint destination, nint newName, int result, nint created) => Ok;

    public int PreCopyItem(uint flags, nint item, nint destination, nint newName) => Ok;

    public int PostCopyItem(uint flags, nint item, nint destination, nint newName, int result, nint created) => Ok;

    public int PostDeleteItem(uint flags, nint item, int result, nint created) => Ok;

    public int PreNewItem(uint flags, nint destination, nint newName) => Ok;

    public int PostNewItem(uint flags, nint destination, nint newName, nint templateName, uint attributes, int result, nint created) => Ok;

    public int UpdateProgress(uint workTotal, uint workSoFar) => Ok;

    public int ResetTimer() => Ok;

    public int PauseTimer() => Ok;

    public int ResumeTimer() => Ok;
}
