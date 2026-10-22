using Tack.App.Pinboard.Contract;
using Tack.Core.Diagnostics;
using Tack.Windows.Sound;

namespace Tack.App.Pinboard.Feedback;

/// <summary>
/// The tock of a pin going in and the pop of one coming out, when sound is
/// on and the board is down. The motions call <see cref="Tock"/> and
/// <see cref="Pop"/> at the moment the pin lands or leaves. A sound still
/// playing is stopped when the board has tucked or sound is turned off, so
/// nothing plays while tucked. UI thread only.
/// </summary>
sealed class Sounds(BoardContext board) : BoardPart(board)
{
    MemorySound? _tock;
    MemorySound? _pop;
    bool _broken;
    bool _played;

    /// <summary>A pin goes into the cork: a muffled tock.</summary>
    public void Tock() => Play(ref _tock, "tock");

    /// <summary>A pin comes out of the cork: a small, dry tick.</summary>
    public void Pop() => Play(ref _pop, "pop");

    public override void Tucked() => Stop();

    public override void LookChanged(BoardLook look)
    {
        if (!look.Sound)
        {
            Stop();
        }
    }

    void Stop()
    {
        if (_played)
        {
            MemorySound.Stop();
            _played = false;
        }
    }

    void Play(ref MemorySound? sound, string name)
    {
        if (!Board.Look.Sound || !Board.State.Revealed || _broken)
        {
            return;
        }
        sound ??= Load(name);
        _played |= sound?.Play() == true;
    }

    /// <summary>The sound's WAV, embedded in the app; null (and no more tries) if it is missing.</summary>
    MemorySound? Load(string name)
    {
        using var stream = typeof(Sounds).Assembly.GetManifestResourceStream($"Tack.Sounds.{name}.wav");
        if (stream is null)
        {
            DevTrace.Write($"sounds: the {name} sound is not in the app");
            _broken = true;
            return null;
        }
        var wave = new byte[stream.Length];
        stream.ReadExactly(wave);
        return new MemorySound(wave);
    }
}
