using Windows.Win32;

namespace Tack.Windows.Clipboard;

/// <summary>Clipboard formats by number and by name, and which of them a snapshot can keep.</summary>
static unsafe class ClipboardFormats
{
    /// <summary>The registered format apps put a PNG file in.</summary>
    public const string Png = "PNG";

    /// <summary>Live OLE objects, by lowercase name: they only make sense with the app that made them.</summary>
    static readonly string[] s_liveObjects =
    [
        "dataobject",
        "ole private data",
        "embed source",
        "embedded object",
        "link source",
        "link source descriptor",
        "object descriptor",
        "ownerlink",
        "objectlink",
        "native",
    ];

    /// <summary>The number Windows gives the named format, the same for every app this session; 0 on failure.</summary>
    public static uint Registered(string name) => PInvoke.RegisterClipboardFormat(name);

    /// <summary>A registered format's name, "" for a standard one.</summary>
    public static string Name(uint format)
    {
        Span<char> name = stackalloc char[128];
        var length = PInvoke.GetClipboardFormatName(format, name);
        return length > 0 ? name[..length].ToString() : "";
    }

    /// <summary>Whether a format can be copied as plain bytes and set again later.</summary>
    public static bool Restorable(uint format) => Restorable(format, format >= 0xC000 ? Name(format) : "");

    /// <summary>As <see cref="Restorable(uint)"/>, with a registered format's name already known.</summary>
    public static bool Restorable(uint format, string name) => format switch
    {
        // GDI handles, or memory holding GDI handles: CF_BITMAP, CF_METAFILEPICT,
        // CF_PALETTE, CF_ENHMETAFILE, CF_OWNERDISPLAY and the CF_DSP forms of GDI ones.
        2 or 3 or 9 or 14 or 0x80 or 0x82 or 0x83 or 0x8E => false,
        // Private and GDI-object ranges: their meaning belongs to the owner.
        >= 0x0200 and <= 0x03FF => false,
        >= 0xC000 and <= 0xFFFF => !s_liveObjects.Contains(name.ToLowerInvariant()),
        _ => true,
    };
}
