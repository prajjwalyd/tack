using Tack.Core.Notes;
using Tack.Core.Prints;
using Tack.Core.Thumbnails;

namespace Tack.Core.Tests.Phone.Serving;

/// <summary>Prints for the phone board's tests, made directly rather than through the board.</summary>
static class TestPrints
{
    public static Print Picture(string id, string path, byte[] jpeg, int width = 800, int height = 600,
        long pinnedAt = 1_000, long? keptAt = null) => new()
        {
            Id = id,
            Path = path,
            Kind = Kind.Image,
            Thumb = new Thumb(jpeg, width, height, null),
            PinnedAt = pinnedAt,
            KeptAt = keptAt,
        };

    public static Print Note(string id, string text, long pinnedAt = 2_000) => new()
    {
        Id = id,
        Path = $"C:\\nowhere\\Notes\\{id}.txt",
        Kind = Kind.Note,
        Note = NoteText.Body(text),
        PinnedAt = pinnedAt,
        Origin = Origin.Capture,
    };
}
