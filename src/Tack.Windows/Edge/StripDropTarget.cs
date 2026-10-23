using System.Runtime.InteropServices.Marshalling;

namespace Tack.Windows.Edge;

/// <summary>
/// The strips' drop target, the one way to hear something carried from
/// another app reach the edge: a drag owns the mouse, so the strip is sent no
/// mouse move. It takes no drop; it only says a drag is over a strip
/// (<see cref="EdgeEvent.DragEntered"/>, <see cref="EdgeEvent.DragLeft"/>),
/// and the board that comes down for it takes the drop. One for every strip,
/// called on the strips' thread.
/// </summary>
[GeneratedComClass]
internal sealed partial class StripDropTarget(Action<EdgeEvent> emit) : IOleDropTarget
{
    const int Ok = 0;

    /// <summary>DROPEFFECT_NONE: nothing would happen if it were let go here.</summary>
    const uint Refused = 0;

    public int DragEnter(nint data, uint keys, long point, ref uint effect)
    {
        effect = Refused;
        emit(EdgeEvent.DragEntered);
        return Ok;
    }

    public int DragOver(uint keys, long point, ref uint effect)
    {
        effect = Refused;
        return Ok;
    }

    public int DragLeave()
    {
        emit(EdgeEvent.DragLeft);
        return Ok;
    }

    public int Drop(nint data, uint keys, long point, ref uint effect)
    {
        effect = Refused;
        emit(EdgeEvent.DragLeft);
        return Ok;
    }
}
