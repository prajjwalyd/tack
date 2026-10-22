using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;

namespace Tack.Windows.Edge;

/// <summary>
/// The strips' OLE drop target: it takes no drop, it only says a drag is over
/// a strip (<see cref="EdgeEvent.DragEntered"/>, <see cref="EdgeEvent.DragLeft"/>).
/// One object for every strip, built by hand with a static vtable: it holds no
/// state, OLE calls it on the strips' thread, and it lives as long as the process,
/// so its reference count is never needed.
/// </summary>
internal static unsafe class StripDropTarget
{
    /// <summary>The object to register with RegisterDragDrop.</summary>
    public static IDropTarget* Instance { get; } = Create();

    static IDropTarget* Create()
    {
        var vtable = (IDropTarget.Vtbl*)NativeMemory.AllocZeroed((nuint)sizeof(IDropTarget.Vtbl));
        vtable->QueryInterface_1 = &QueryInterface;
        vtable->AddRef_2 = &AddRef;
        vtable->Release_3 = &Release;
        vtable->DragEnter_4 = &DragEnter;
        vtable->DragOver_5 = &DragOver;
        vtable->DragLeave_6 = &DragLeave;
        vtable->Drop_7 = &Drop;
        // A COM object is a pointer to its vtable.
        var target = (void**)NativeMemory.Alloc((nuint)sizeof(void*));
        *target = vtable;
        return (IDropTarget*)target;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static HRESULT QueryInterface(IDropTarget* self, Guid* iid, void** result)
    {
        if (*iid == IDropTarget.IID_Guid || *iid == IUnknown.IID_Guid)
        {
            *result = self;
            return HRESULT.S_OK;
        }
        *result = null;
        return HRESULT.E_NOINTERFACE;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint AddRef(IDropTarget* self) => 2;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint Release(IDropTarget* self) => 1;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static HRESULT DragEnter(IDropTarget* self, IDataObject* data, MODIFIERKEYS_FLAGS keys, POINTL point, DROPEFFECT* effect)
    {
        Refuse(effect);
        StripThread.Current?.Emit(EdgeEvent.DragEntered);
        return HRESULT.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static HRESULT DragOver(IDropTarget* self, MODIFIERKEYS_FLAGS keys, POINTL point, DROPEFFECT* effect)
    {
        Refuse(effect);
        return HRESULT.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static HRESULT DragLeave(IDropTarget* self)
    {
        StripThread.Current?.Emit(EdgeEvent.DragLeft);
        return HRESULT.S_OK;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static HRESULT Drop(IDropTarget* self, IDataObject* data, MODIFIERKEYS_FLAGS keys, POINTL point, DROPEFFECT* effect)
    {
        Refuse(effect);
        StripThread.Current?.Emit(EdgeEvent.DragLeft);
        return HRESULT.S_OK;
    }

    static void Refuse(DROPEFFECT* effect)
    {
        if (effect != null)
        {
            *effect = DROPEFFECT.DROPEFFECT_NONE;
        }
    }
}
