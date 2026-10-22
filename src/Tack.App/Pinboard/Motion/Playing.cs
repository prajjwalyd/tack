using Microsoft.UI.Composition;

namespace Tack.App.Pinboard.Motion;

/// <summary>
/// A motion the compositor is running on one layer, from <see cref="Keyframer"/>.
/// Cancelling stops each property where it is and skips the motion's ending.
/// A later motion on the same property takes that property over.
/// </summary>
sealed class Playing
{
    readonly Keyframer _owner;

    internal Playing(Keyframer owner, Visual visual, List<string> properties) =>
        (_owner, Visual, Properties) = (owner, visual, properties);

    /// <summary>Ran to its end, or was cancelled or taken over.</summary>
    public bool Over { get; private set; }

    public bool Cancelled { get; private set; }

    internal Visual Visual { get; }

    /// <summary>The properties it still drives.</summary>
    internal List<string> Properties { get; }

    /// <summary>Stops it now, where it is. Its ending (a fill reset, the done callback) does not run.</summary>
    public void Cancel()
    {
        if (Over)
        {
            return;
        }
        (Cancelled, Over) = (true, true);
        foreach (var property in Properties)
        {
            Visual.StopAnimation(property);
        }
        _owner.Forget(this);
    }

    /// <summary>Another motion took <paramref name="property"/> over; with nothing left to drive, this one is cancelled.</summary>
    internal void Lose(string property)
    {
        Properties.Remove(property);
        if (Properties.Count == 0)
        {
            (Cancelled, Over) = (true, true);
            _owner.Forget(this);
        }
    }

    /// <summary>The batch completed: true if it ran to its end, false if it had been cancelled.</summary>
    internal bool Finish()
    {
        if (Cancelled)
        {
            return false;
        }
        Over = true;
        _owner.Forget(this);
        return true;
    }
}
