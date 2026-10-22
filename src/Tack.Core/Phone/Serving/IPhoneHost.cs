using Tack.Core.NetBird;
using Tack.Core.Prints;

namespace Tack.Core.Phone.Serving;

/// <summary>
/// What the phone board's routes need from the running app: the user's
/// answer about devices, the board's prints, and pinning. Implemented by the
/// app over its shared state; tests hand in a fake. Called from the server's
/// connections, on the thread pool, several at once.
/// </summary>
public interface IPhoneHost
{
    /// <summary>Whether the user allowed the device with WireGuard key <paramref name="key"/>.</summary>
    bool Allows(string key);

    /// <summary>
    /// A request from the page on <paramref name="peer"/>: let in, made to wait
    /// (asking the user if it is the first request, with a fresh code), turned
    /// away, or told the PC is busy (<see cref="PhoneGate.Check"/>).
    /// </summary>
    Verdict Admit(Peer peer);

    /// <summary>A turned-down device asks again from its page (<see cref="PhoneGate.AskAgain"/>).</summary>
    Verdict AskAgain(Peer peer);

    /// <summary>The prints on the board now, in the board's order: a copy, safe to read without a lock.</summary>
    IReadOnlyList<Print> Prints();

    /// <summary>The print with <paramref name="id"/>, if it is on the board.</summary>
    Print? Find(string id);

    /// <summary>Whether a note with exactly this text is on the board.</summary>
    bool HasNote(string text);

    /// <summary>Finishes once the file at <paramref name="path"/> is written, if it is a capture still being saved (a few seconds at most).</summary>
    Task WaitSavedAsync(string path);

    /// <summary>Pins <paramref name="text"/> as a note (or a link), as the pin shortcut would.</summary>
    Task PinTextAsync(string text);

    /// <summary>Pins a PNG or JPEG sent from a device, as a dropped picture is pinned; false if it could not be.</summary>
    Task<bool> PinImageAsync(string name, byte[] bytes);
}
