using System.Runtime.InteropServices.ComTypes;
using Windows.Win32;

namespace Tack.Windows.Capture;

/// <summary>The CPU time the calling thread has used, for timing a lookup in the log.</summary>
static unsafe class ThreadCpu
{
    /// <summary>Kernel and user time so far; zero if Windows will not say.</summary>
    public static TimeSpan Used()
    {
        FILETIME created, exited, kernel, user;
        if (!PInvoke.GetThreadTimes(PInvoke.GetCurrentThread(), &created, &exited, &kernel, &user))
        {
            return TimeSpan.Zero;
        }
        // FILETIME counts 100 ns ticks, as TimeSpan does.
        return TimeSpan.FromTicks(Ticks(kernel) + Ticks(user));
    }

    static long Ticks(FILETIME time) => (long)((ulong)(uint)time.dwHighDateTime << 32 | (uint)time.dwLowDateTime);
}
