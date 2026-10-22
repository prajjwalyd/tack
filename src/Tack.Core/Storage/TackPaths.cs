namespace Tack.Core.Storage;

/// <summary>
/// Where Tack keeps its files: the board in the roaming folder, captures and
/// notes in the local one, as Tack 0.2 did.
/// </summary>
/// <param name="Roaming">Holds board.json. Normally %APPDATA%\Tack.</param>
/// <param name="Local">Holds Captures and Notes. Normally %LOCALAPPDATA%\Tack.</param>
public sealed record TackPaths(string Roaming, string Local)
{
    /// <summary>Points Tack at another folder instead, holding Roaming and Local.</summary>
    public const string HomeVariable = "TACK_HOME";

    /// <summary>Where a development build keeps its files when TACK_HOME is not set.</summary>
    public const string DevelopmentFolder = "Tack.Dev";

    public string BoardFile => Path.Combine(Roaming, "board.json");

    public string Captures => Path.Combine(Local, "Captures");

    public string Notes => Path.Combine(Local, "Notes");

    /// <summary>
    /// TACK_HOME if it is set. Otherwise a development build gets a scratch
    /// folder of its own, so it can never touch the real board, and a release
    /// build gets the real folders. Without APPDATA or LOCALAPPDATA the folders
    /// fall back to .\Tack, as Tack 0.2's did.
    /// </summary>
    public static TackPaths Resolve(Func<string, string?> environment, bool development)
    {
        var home = environment(HomeVariable);
        if (!string.IsNullOrWhiteSpace(home))
        {
            return Under(home);
        }
        var local = environment("LOCALAPPDATA");
        if (development)
        {
            return Under(Path.Combine(Folder(local), DevelopmentFolder));
        }
        return new TackPaths(Path.Combine(Folder(environment("APPDATA")), "Tack"), Path.Combine(Folder(local), "Tack"));
    }

    static TackPaths Under(string home) => new(Path.Combine(home, "Roaming"), Path.Combine(home, "Local"));

    static string Folder(string? value) => string.IsNullOrWhiteSpace(value) ? "." : value;
}
