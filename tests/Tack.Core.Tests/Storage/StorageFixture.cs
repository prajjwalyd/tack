using System.Text;
using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Storage;

namespace Tack.Core.Tests.Storage;

/// <summary>Helpers shared by the board.json tests.</summary>
static class StorageFixture
{
    public const long Now = 1_800_000_000_000;

    public static SavedPrint Saved(string path, Origin origin, long pinnedAt, long? keptAt) =>
        new(path, Kind.Image, origin, pinnedAt, keptAt);

    public static (Saved Saved, Loaded Loaded) Parse(string json) => BoardFile.Parse(Encoding.UTF8.GetBytes(json), Now);

    /// <summary>A print on the board for <paramref name="name"/> in <paramref name="folder"/>; a .txt is a note.</summary>
    public static Print Print(string folder, string id, string name, Origin origin, long pinnedAt, long? keptAt)
    {
        var note = name.EndsWith(".txt", StringComparison.Ordinal);
        return new Print
        {
            Id = id,
            Path = Path.Combine(folder, name),
            Name = name,
            Kind = note ? Kind.Note : Kind.Image,
            Note = note ? new NoteBody($"the words of {name}", null, null, false) : null,
            PinnedAt = pinnedAt,
            KeptAt = keptAt,
            Origin = origin,
        };
    }

    /// <summary>Writes board.json as the app's saver does.</summary>
    public static void Save(string file, IReadOnlyList<Print> prints, Tack.Core.Settings.TackSettings settings) =>
        BoardFile.Write(file, Snapshot.Of(prints, [], settings));

    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name)).TrimEnd('\n', '\r');
}
