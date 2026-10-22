using Tack.Core.NetBird;
using Tack.Core.Phone;
using Tack.Core.Phone.Serving;
using Tack.Core.Prints;

namespace Tack.Core.Tests.Phone.Serving;

/// <summary>The app as the routes see it, with answers a test sets and a record of what was asked.</summary>
sealed class FakeHost : IPhoneHost
{
    public HashSet<string> Allowed { get; } = [];

    /// <summary>What the gate says to a request from the page.</summary>
    public Verdict AdmitSays { get; set; } = Verdict.Allowed.Instance;

    public Verdict AskAgainSays { get; set; } = Verdict.Allowed.Instance;

    public List<Print> Board { get; } = [];

    public List<string> Admitted { get; } = [];

    public List<string> Waited { get; } = [];

    public List<string> PinnedTexts { get; } = [];

    public List<(string Name, byte[] Bytes)> PinnedImages { get; } = [];

    public bool ImagePins { get; set; } = true;

    /// <summary>When set, an image pin waits for it, so a test can look while the pin is under way.</summary>
    public Task? ImagePinWaitsFor { get; set; }

    public bool Allows(string key) => Allowed.Contains(key);

    public Verdict Admit(Peer peer)
    {
        Admitted.Add(peer.Key);
        return AdmitSays;
    }

    public Verdict AskAgain(Peer peer) => AskAgainSays;

    public IReadOnlyList<Print> Prints() => [.. Board];

    public Print? Find(string id) => Board.Find(p => p.Id == id);

    public bool HasNote(string text) => Board.Exists(p => p.Note?.Text == text);

    public Task WaitSavedAsync(string path)
    {
        Waited.Add(path);
        return Task.CompletedTask;
    }

    public Task PinTextAsync(string text)
    {
        PinnedTexts.Add(text);
        return Task.CompletedTask;
    }

    public async Task<bool> PinImageAsync(string name, byte[] bytes)
    {
        if (ImagePinWaitsFor is { } wait)
        {
            await wait;
        }
        PinnedImages.Add((name, bytes));
        return ImagePins;
    }
}
