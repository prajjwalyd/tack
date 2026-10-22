using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;

namespace Tack.Windows.Sound;

/// <summary>
/// A short WAV played from memory, without waiting and never falling back to
/// the system's default sound. Windows plays one such sound at a time, so a
/// new one cuts off the one before. The bytes are copied once to memory that
/// lives as long as the process, since playing reads them after the call
/// returns. UI thread only.
/// </summary>
public sealed unsafe class MemorySound
{
    readonly byte* _wave;

    /// <param name="wave">A whole RIFF WAV file.</param>
    public MemorySound(ReadOnlySpan<byte> wave)
    {
        _wave = (byte*)NativeMemory.Alloc((nuint)wave.Length);
        wave.CopyTo(new Span<byte>(_wave, wave.Length));
    }

    /// <summary>Starts playing it, cutting off any sound still playing. Returns whether Windows took it.</summary>
    public bool Play() =>
        PInvoke.PlaySound(new PCWSTR((char*)_wave), HMODULE.Null, SND_FLAGS.SND_MEMORY | SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_NODEFAULT);

    /// <summary>Stops whatever sound is playing; nothing playing is fine.</summary>
    public static void Stop() => PInvoke.PlaySound(default(PCWSTR), HMODULE.Null, 0);
}
