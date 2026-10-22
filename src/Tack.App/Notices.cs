using System.Threading.Channels;
using Tack.App.Pinboard.Contract;
using Tack.Core.Diagnostics;

namespace Tack.App;

/// <summary>
/// What the board says, briefly, when something could not be pinned. A tucked
/// board comes down for a notice unless something is full screen, in which
/// case it is dropped; the board holds one that arrives while it is still
/// swinging down. Notices are shown one at a time in the order asked, so the
/// last one asked is the one that stays.
/// </summary>
sealed class Notices
{
    public const string NothingSelected = "Nothing selected";
    public const string AlreadyPinned = "Already pinned";
    public const string Private = "Not pinned: marked private";
    public const string NotPinnable = "Only images and text can be pinned";
    public const string BadImage = "Can't pin that image";
    public const string Failed = "Couldn't pin that";
    public const string NotRestored = "Couldn't restore your clipboard";

    readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    readonly AppState _state;
    readonly Reveal _reveal;
    readonly BoardFeed _feed;

    public Notices(AppState state, Reveal reveal, BoardFeed feed)
    {
        (_state, _reveal, _feed) = (state, reveal, feed);
        Background.Run("showing notices", ShowAllAsync);
    }

    /// <summary>Shows <paramref name="text"/> under the board, which comes down briefly for it. From any thread; returns at once.</summary>
    public void Show(string text) => _queue.Writer.TryWrite(text);

    async Task ShowAllAsync()
    {
        await foreach (var text in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            bool shown;
            lock (_state.Gate)
            {
                shown = _state.View.Shown;
            }
            if (!shown)
            {
                if (!await _reveal.MayRevealNewAsync().ConfigureAwait(false))
                {
                    DevTrace.Write($"notice: {text}; not shown, something is full screen");
                    continue;
                }
                _reveal.ForNotice();
            }
            DevTrace.Write($"notice: {text}");
            _feed.RaiseNotice(text);
        }
    }
}
