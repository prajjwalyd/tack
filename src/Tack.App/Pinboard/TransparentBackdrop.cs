using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Tack.App.Pinboard;

/// <summary>
/// WinUI has no transparent window (microsoft-ui-xaml #1247, open since 2019).
/// The known workaround: a system backdrop whose brush is fully transparent,
/// with <see cref="Tack.Windows.Windowing.SeeThrough"/> for the Win32 half.
/// </summary>
sealed partial class TransparentBackdrop : SystemBackdrop
{
    static global::Windows.UI.Composition.Compositor? s_compositor;
    static nint s_queueController;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        s_compositor ??= CreateSystemCompositor();
        target.SystemBackdrop = s_compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        base.OnTargetDisconnected(target);
    }

    /// <summary>
    /// The backdrop brush comes from the system compositor, which needs a
    /// Windows.System dispatcher queue on this thread. WinUI's own queue is a
    /// different type, so make one if there is none.
    /// </summary>
    static global::Windows.UI.Composition.Compositor CreateSystemCompositor()
    {
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() == null)
        {
            var options = new DispatcherQueueOptions
            {
                dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                threadType = 2, // DQTYPE_THREAD_CURRENT
                apartmentType = 0, // DQTAT_COM_NONE
            };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out s_queueController));
        }
        return new global::Windows.UI.Composition.Compositor();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
}
