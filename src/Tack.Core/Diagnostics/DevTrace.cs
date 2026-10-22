using System.Diagnostics;

namespace Tack.Core.Diagnostics;

/// <summary>
/// Tracing for development builds only: calls compile away wherever DEBUG is
/// not defined, so a release build writes nothing. Never trace window titles,
/// clipboard contents, note text or screen grabs.
/// </summary>
public static class DevTrace
{
    [Conditional("DEBUG")]
    public static void Write(string message)
    {
        var line = $"tack {DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}";
        Debug.WriteLine(line);
        Console.Error.WriteLine(line);
    }
}
