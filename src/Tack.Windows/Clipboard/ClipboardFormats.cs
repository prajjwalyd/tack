namespace Tack.Windows.Clipboard;

/// <summary>Clipboard formats Tack names, and the ones a saved clipboard leaves out.</summary>
static class ClipboardFormats
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

    /// <summary>Whether a format, by name, is a live OLE object, which cannot be kept and put back.</summary>
    public static bool IsLiveObject(string name) => s_liveObjects.Contains(name.ToLowerInvariant());
}
