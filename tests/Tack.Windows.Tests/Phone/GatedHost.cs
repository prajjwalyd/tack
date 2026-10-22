using Tack.Core.NetBird;
using Tack.Core.Phone;
using Tack.Core.Phone.Serving;
using Tack.Core.Prints;

namespace Tack.Windows.Tests.Phone;

/// <summary>
/// The app as the routes see it, with the real gate: a device asks, the test
/// answers as the person would, and the allowed devices are kept as the
/// settings keep them. The board is empty; pinned text is recorded.
/// </summary>
sealed class GatedHost : IPhoneHost
{
    readonly Lock _lock = new();
    readonly PhoneGate _gate = new();
    PhoneSettings _settings = new() { On = true };

    public List<string> PinnedTexts { get; } = [];

    /// <summary>The devices waiting for an answer.</summary>
    public IReadOnlyList<Asking> Pending
    {
        get
        {
            lock (_lock)
            {
                return [.. _gate.Pending];
            }
        }
    }

    /// <summary>Answers for the person, and saves an allowed device as the settings would.</summary>
    public void Answer(string key, bool allow)
    {
        lock (_lock)
        {
            if (_gate.Answer(key, allow, Now) is { } name && allow)
            {
                _settings = _settings.Allow(key, name, (ulong)Now);
            }
        }
    }

    public bool Allows(string key)
    {
        lock (_lock)
        {
            return _settings.Allows(key);
        }
    }

    public Verdict Admit(Peer peer)
    {
        lock (_lock)
        {
            return _gate.Check(_settings, WhoIs(peer), "4821", Now);
        }
    }

    public Verdict AskAgain(Peer peer)
    {
        lock (_lock)
        {
            return _gate.AskAgain(_settings, WhoIs(peer), "4821", Now);
        }
    }

    public IReadOnlyList<Print> Prints() => [];

    public Print? Find(string id) => null;

    public bool HasNote(string text) => PinnedTexts.Contains(text);

    public Task WaitSavedAsync(string path) => Task.CompletedTask;

    public Task PinTextAsync(string text)
    {
        PinnedTexts.Add(text);
        return Task.CompletedTask;
    }

    public Task<bool> PinImageAsync(string name, byte[] bytes) => Task.FromResult(false);

    static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    static Who WhoIs(Peer peer) => new(peer.Key, peer.ShortName, peer.Fqdn, peer.Ip.ToString());
}
