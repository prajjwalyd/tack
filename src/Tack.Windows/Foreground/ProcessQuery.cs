using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Tack.Windows.Foreground;

/// <summary>A window's process, open only to ask about it; closed when disposed.</summary>
sealed unsafe class ProcessQuery : IDisposable
{
    readonly HANDLE _handle;

    ProcessQuery(HANDLE handle) => _handle = handle;

    /// <summary>The process behind a window, or null if there is none or Windows will not open it.</summary>
    public static ProcessQuery? Of(HWND hwnd)
    {
        if (WindowFacts.ProcessId(hwnd) is not { } pid)
        {
            return null;
        }
        var handle = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        return handle.IsNull ? null : new ProcessQuery(handle);
    }

    /// <summary>The full path of its program.</summary>
    public string? Image()
    {
        var buffer = stackalloc char[1024];
        uint length = 1024;
        return PInvoke.QueryFullProcessImageName(_handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, &length)
            ? new string(buffer, 0, (int)length)
            : null;
    }

    /// <summary>Its package family, for a packaged app; null for any other.</summary>
    public string? Family()
    {
        uint length = 0;
        if (PInvoke.GetPackageFamilyName(_handle, &length, default) != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            return null;
        }
        var buffer = new char[length];
        fixed (char* chars = buffer)
        {
            if (PInvoke.GetPackageFamilyName(_handle, &length, chars) != WIN32_ERROR.ERROR_SUCCESS)
            {
                return null;
            }
        }
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }

    public void Dispose() => PInvoke.CloseHandle(_handle);
}
