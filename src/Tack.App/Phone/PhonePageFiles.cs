using Tack.Core.Phone.Serving;

namespace Tack.App.Phone;

/// <summary>The phone page's files (assets/phone), embedded in the app and served unchanged.</summary>
static class PhonePageFiles
{
    /// <summary>Reads the four files. Throws when one is missing from the app, which a build never leaves out.</summary>
    public static PhoneFiles Read() => new(Read("index.html"), Read("phone.js"), Read("phone.css"), Read("icon.png"));

    static byte[] Read(string name)
    {
        using var stream = typeof(PhonePageFiles).Assembly.GetManifestResourceStream($"Tack.Phone.{name}")
            ?? throw new InvalidOperationException($"the page's {name} is not in the app");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
