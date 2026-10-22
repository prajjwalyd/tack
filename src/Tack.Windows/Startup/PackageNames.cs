using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Tack.Windows.Startup;

/// <summary>The MSIX package identities of processes, by their full package names.</summary>
internal static unsafe class PackageNames
{
    /// <summary>The package this process runs as, or null when it has none.</summary>
    public static string? Own()
    {
        uint length = 0;
        // Anything but "the buffer is too small" means no package, usually APPMODEL_ERROR_NO_PACKAGE.
        if (PInvoke.GetCurrentPackageFullName(&length, default) != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
        {
            return null;
        }
        var buffer = new char[length];
        fixed (char* name = buffer)
        {
            return PInvoke.GetCurrentPackageFullName(&length, name) == WIN32_ERROR.ERROR_SUCCESS ? Text(buffer) : null;
        }
    }

    /// <summary>The package the process <paramref name="processId"/> runs as, or null when it has none or cannot be asked.</summary>
    public static string? Of(int processId)
    {
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (process.IsNull)
        {
            return null;
        }
        try
        {
            uint length = 0;
            if (PInvoke.GetPackageFullName(process, &length, default) != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
            {
                return null;
            }
            var buffer = new char[length];
            fixed (char* name = buffer)
            {
                return PInvoke.GetPackageFullName(process, &length, name) == WIN32_ERROR.ERROR_SUCCESS ? Text(buffer) : null;
            }
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    /// <summary>The name up to its terminating zero.</summary>
    static string Text(char[] buffer)
    {
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }
}
