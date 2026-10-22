using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;

namespace Tack.Windows.Startup;

/// <summary>Which process started which, as far as Windows still knows.</summary>
internal static unsafe class ProcessParents
{
    /// <summary>Every running process's parent id, by process id, from one snapshot. Empty if Windows will not take one.</summary>
    public static Dictionary<int, int> Snapshot()
    {
        var parents = new Dictionary<int, int>();
        var snapshot = PInvoke.CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        // INVALID_HANDLE_VALUE is a failure.
        if (snapshot.IsNull || (nint)snapshot.Value == -1)
        {
            return parents;
        }
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            for (var more = PInvoke.Process32FirstW(snapshot, &entry); more; more = PInvoke.Process32NextW(snapshot, &entry))
            {
                parents[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
            }
        }
        finally
        {
            PInvoke.CloseHandle(snapshot);
        }
        return parents;
    }
}
