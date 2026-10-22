using Tack.Core.Captures;
using Tack.Core.NetBird;
using Tack.Core.Phone;
using Tack.Core.Phone.Serving;
using Tack.Core.Prints;

namespace Tack.App.Phone;

/// <summary>
/// What the phone board's routes get from the app: the board read under
/// <see cref="AppState.Gate"/> and handed over as copies, captures waited for
/// while they are written, and pins made as the pin shortcut and a drop make
/// them, notices and reveal included. The person's answer about devices is
/// <see cref="PhoneLink"/>'s. Called from the server's connections, on the
/// thread pool, several at once.
/// </summary>
sealed class PhoneHost(AppState state, PendingSaves saves, Pinning pinning, ImageDrops drops, PhoneLink link) : IPhoneHost
{
    public bool Allows(string key)
    {
        lock (state.Gate)
        {
            return state.Settings.Phone.Allows(key);
        }
    }

    public Verdict Admit(Peer peer) => link.Admit(peer);

    public Verdict AskAgain(Peer peer) => link.AskAgain(peer);

    public IReadOnlyList<Print> Prints()
    {
        lock (state.Gate)
        {
            return [.. state.Board.Prints];
        }
    }

    public Print? Find(string id)
    {
        lock (state.Gate)
        {
            return state.Board.Find(id);
        }
    }

    public bool HasNote(string text)
    {
        lock (state.Gate)
        {
            return state.Board.FindNote(text) is not null;
        }
    }

    public Task WaitSavedAsync(string path) => saves.WaitAsync(path, PendingSaves.Patience);

    public Task PinTextAsync(string text) => pinning.PinPhoneTextAsync(text);

    public Task<bool> PinImageAsync(string name, byte[] bytes) => drops.PinAsync(name, bytes);
}
