using System.Runtime.InteropServices;
using Windows.Foundation;

namespace Tack.Windows.Clipboard;

/// <summary>
/// A thread of its own for each use of Windows' clipboard. Windows answers
/// only a thread that is single-threaded for COM, and what it hands back
/// belongs to that thread, so one use runs start to end on one thread.
/// Whatever Tack puts on the clipboard is drawn in full before the thread
/// ends, and its window ends with it: no Tack thread is left owning the
/// clipboard, so no app that copies next ever waits on Tack to let it go.
/// </summary>
static class ClipboardThread
{
    /// <summary>A use is tried again for a moment while another app has the clipboard open.</summary>
    const int Tries = 4;
    static readonly TimeSpan s_gap = TimeSpan.FromMilliseconds(60);

    /// <summary>Runs <paramref name="body"/> on a new thread, without holding the caller's.</summary>
    public static Task<T> RunAsync<T>(Func<T> body)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(body());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        { IsBackground = true, Name = "tack-clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>
    /// Runs <paramref name="use"/>, and again a few times, a moment apart, while
    /// Windows says the clipboard is busy; on the clipboard thread. Throws
    /// <see cref="COMException"/> if it stays busy.
    /// </summary>
    public static T Retrying<T>(Func<T> use)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return use();
            }
            catch (COMException) when (attempt < Tries)
            {
                Thread.Sleep(s_gap);
            }
        }
    }

    /// <summary>Waits on this thread for one of Windows' answers that come later.</summary>
    public static T Wait<T>(IAsyncOperation<T> operation) => operation.AsTask().GetAwaiter().GetResult();
}
