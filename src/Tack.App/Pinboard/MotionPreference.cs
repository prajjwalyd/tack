using Microsoft.UI.Dispatching;
using Windows.UI.ViewManagement;

namespace Tack.App.Pinboard;

/// <summary>
/// Whether Windows asks for less motion ("Animation effects" off), followed
/// live. <see cref="Changed"/> fires on the UI thread.
/// </summary>
sealed class MotionPreference
{
    readonly UISettings _settings = new();

    public MotionPreference(DispatcherQueue queue)
    {
        Reduced = !_settings.AnimationsEnabled;
        // Raised on another thread.
        _settings.AnimationsEnabledChanged += (_, _) => queue.TryEnqueue(() =>
        {
            var reduced = !_settings.AnimationsEnabled;
            if (reduced != Reduced)
            {
                Reduced = reduced;
                Changed?.Invoke();
            }
        });
    }

    public event Action? Changed;

    public bool Reduced { get; private set; }
}
