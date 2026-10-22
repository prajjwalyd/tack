using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Tack.Windows.Files;

/// <summary>
/// The shell's <c>IFileOperationProgressSink</c>, declared here so a C# object
/// can implement it under Native AOT. The methods are in v-table order;
/// shell items and strings arrive as raw pointers, and every method returns
/// an HRESULT.
/// </summary>
[GeneratedComInterface]
[Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2")]
internal partial interface IOperationProgressSink
{
    [PreserveSig]
    int StartOperations();

    [PreserveSig]
    int FinishOperations(int result);

    [PreserveSig]
    int PreRenameItem(uint flags, nint item, nint newName);

    [PreserveSig]
    int PostRenameItem(uint flags, nint item, nint newName, int result, nint created);

    [PreserveSig]
    int PreMoveItem(uint flags, nint item, nint destination, nint newName);

    [PreserveSig]
    int PostMoveItem(uint flags, nint item, nint destination, nint newName, int result, nint created);

    [PreserveSig]
    int PreCopyItem(uint flags, nint item, nint destination, nint newName);

    [PreserveSig]
    int PostCopyItem(uint flags, nint item, nint destination, nint newName, int result, nint created);

    [PreserveSig]
    int PreDeleteItem(uint flags, nint item);

    [PreserveSig]
    int PostDeleteItem(uint flags, nint item, int result, nint created);

    [PreserveSig]
    int PreNewItem(uint flags, nint destination, nint newName);

    [PreserveSig]
    int PostNewItem(uint flags, nint destination, nint newName, nint templateName, uint attributes, int result, nint created);

    [PreserveSig]
    int UpdateProgress(uint workTotal, uint workSoFar);

    [PreserveSig]
    int ResetTimer();

    [PreserveSig]
    int PauseTimer();

    [PreserveSig]
    int ResumeTimer();
}
