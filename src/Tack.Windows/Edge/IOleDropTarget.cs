using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Tack.Windows.Edge;

/// <summary>
/// OLE's <c>IDropTarget</c>, declared here so a C# object can be one under
/// Native AOT. The methods are in v-table order; the data object arrives as a
/// raw pointer, the point (a POINTL, passed by value) as one 64-bit value, and
/// every method returns an HRESULT.
/// </summary>
[GeneratedComInterface]
[Guid("00000122-0000-0000-C000-000000000046")]
internal partial interface IOleDropTarget
{
    [PreserveSig]
    int DragEnter(nint data, uint keys, long point, ref uint effect);

    [PreserveSig]
    int DragOver(uint keys, long point, ref uint effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop(nint data, uint keys, long point, ref uint effect);
}
