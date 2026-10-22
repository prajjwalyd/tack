namespace Tack.Windows.Snips;

/// <summary>
/// The rules for trusting a program as Snipping Tool: by its package family,
/// the same on every PC and signed into the package, or by the full path of
/// its program. A name alone would trust any program called SnippingTool.exe.
/// </summary>
static class TrustedSnipper
{
    /// <summary>Snipping Tool's package family: its publisher's signature is part of it, so no other app can have it.</summary>
    public const string Family = "Microsoft.ScreenSketch_8wekyb3d8bbwe";

    /// <summary>
    /// The programs that put Win + Shift + S captures on the clipboard, trusted
    /// by name only where only Windows installs programs (the old Snipping Tool
    /// in System32, the capture overlay in SystemApps).
    /// </summary>
    static readonly string[] s_snippers = ["snippingtool.exe", "screenclippinghost.exe"];

    static readonly Lazy<IReadOnlyList<string>> s_folders = new(FindFolders);

    /// <summary>
    /// Where only Windows puts programs: System32, SystemApps, and the folder
    /// Store packages are installed to; each lowercase, ending in a backslash.
    /// </summary>
    public static IReadOnlyList<string> Folders => s_folders.Value;

    /// <summary>Whether <paramref name="path"/> is one of the snipping programs inside one of <paramref name="folders"/>.</summary>
    public static bool IsTrusted(string path, IReadOnlyList<string> folders)
    {
        path = path.ToLowerInvariant();
        return s_snippers.Contains(Path.GetFileName(path))
            && !path.Contains(@"\..\", StringComparison.Ordinal)
            && folders.Any(folder => path.StartsWith(folder, StringComparison.Ordinal));
    }

    static IReadOnlyList<string> FindFolders()
    {
        var folders = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0)
        {
            windows = windows.TrimEnd('\\').ToLowerInvariant();
            folders.Add($@"{windows}\system32\");
            folders.Add($@"{windows}\systemapps\");
        }
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (programFiles.Length > 0)
        {
            folders.Add($@"{programFiles.TrimEnd('\\').ToLowerInvariant()}\windowsapps\");
        }
        return folders;
    }
}
