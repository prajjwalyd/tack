using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Tack.Core.Diagnostics;

namespace Tack.Windows.Shell;

/// <summary>
/// Hands files, folders and web links to the apps Windows has registered for
/// them. The shell may load extensions that need COM and can take a moment,
/// so each request runs on a short-lived thread of its own and the caller
/// never waits. Safe to call from any thread.
/// </summary>
public static class ShellLauncher
{
    /// <summary>Opens a file with its default app, or a folder in Explorer.</summary>
    public static void Open(string path) => Start(path, ["open"]);

    /// <summary>The "edit" verb, which is Paint for images. Opens normally if no editor is registered.</summary>
    public static void Edit(string path) => Start(path, ["edit", "open"]);

    /// <summary>
    /// Opens a web link in the default browser. Anything but an http or https
    /// link is refused: handed to the shell, it could start a program.
    /// </summary>
    public static void OpenUrl(string url)
    {
        if (!IsWebLink(url))
        {
            DevTrace.Write("shell: not opening a link that is not http or https");
            return;
        }
        Start(url, ["open"]);
    }

    /// <summary>An http or https link with no white space in it.</summary>
    internal static bool IsWebLink(string url) =>
        (StartsWithAscii(url, "https://") || StartsWithAscii(url, "http://")) && !url.Any(char.IsWhiteSpace);

    /// <summary>
    /// How one verb is asked for. Only the last try may show Windows' own
    /// error or "open with" dialog; an earlier one falls through quietly.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string target, string verb, bool lastTry) => new(target)
    {
        UseShellExecute = true,
        Verb = verb,
        ErrorDialog = lastTry,
    };

    /// <summary>
    /// ASCII letters only, so look-alikes such as the long s, which a culture
    /// or Unicode case rule would fold to "s", never pass.
    /// </summary>
    static bool StartsWithAscii(string text, string prefix) =>
        text.Length >= prefix.Length && Ascii.EqualsIgnoreCase(text.AsSpan(0, prefix.Length), prefix);

    static void Start(string target, string[] verbs)
    {
        // The shell wants a single-threaded apartment for its extensions.
        var thread = new Thread(() => Run(target, verbs)) { IsBackground = true, Name = "tack-shell" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>Tries each verb in turn until one starts something.</summary>
    static void Run(string target, string[] verbs)
    {
        for (var i = 0; i < verbs.Length; i++)
        {
            try
            {
                using var started = Process.Start(StartInfo(target, verbs[i], lastTry: i == verbs.Length - 1));
                return;
            }
            catch (Win32Exception e)
            {
                DevTrace.Write($"shell: {verbs[i]} failed: {e.Message}");
            }
        }
    }
}
