using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using WinRT;

namespace Tack.Windows.Clipboard;

/// <summary>
/// The privacy markers apps put beside a copy. Either of the first two means
/// "do not look"; the last, set to 0, keeps it out of clipboard history. A
/// password manager sets them, and Tack honours all three conventions.
/// </summary>
static class PrivacyMarkers
{
    public static readonly string[] Names =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
        "CanIncludeInClipboardHistory",
    ];

    /// <summary>Where the history marker, which says yes or no, starts in <see cref="Names"/>.</summary>
    public const int FirstAllowance = 2;

    /// <summary>
    /// Whether whoever copied what <paramref name="view"/> holds marked it
    /// private. Presence is what counts, as an app may set a marker with no
    /// data. On the clipboard thread.
    /// </summary>
    public static bool IsPrivate(DataPackageView view)
    {
        for (var i = 0; i < Names.Length; i++)
        {
            var present = view.Contains(Names[i]);
            if (Counts(i, present, present && i >= FirstAllowance ? Data(view, Names[i]) : null))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether marker <paramref name="index"/> marks the copy: present, and for
    /// history, not saying 1 (allowed).
    /// </summary>
    public static bool Counts(int index, bool present, byte[]? data) =>
        present && (index < FirstAllowance || data is null || !data.AsSpan().StartsWith((ReadOnlySpan<byte>)[1, 0, 0, 0]));

    static byte[]? Data(DataPackageView view, string name)
    {
        try
        {
            return ClipboardView.Bytes(ClipboardThread.Wait(view.GetDataAsync(name)).As<IRandomAccessStream>());
        }
        catch (COMException)
        {
            return null;
        }
    }
}
