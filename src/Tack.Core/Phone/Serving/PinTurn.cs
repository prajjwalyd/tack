namespace Tack.Core.Phone.Serving;

/// <summary>One of the <see cref="PinLimits.MaxPinning"/> turns to pin, given back once when disposed.</summary>
internal sealed class PinTurn(Action giveBack) : IDisposable
{
    int _given;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _given, 1) == 0)
        {
            giveBack();
        }
    }
}
