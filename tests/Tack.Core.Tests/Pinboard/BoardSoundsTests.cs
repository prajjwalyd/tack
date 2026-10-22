using System.Buffers.Binary;
using System.Text;

namespace Tack.Core.Tests.Pinboard;

/// <summary>The board's two sounds as scripts/sounds/render.cs writes them: small, quiet, click-free PCM the app plays from memory.</summary>
public class BoardSoundsTests
{
    static byte[] Wave(string name)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Tack.slnx")))
        {
            folder = folder.Parent;
        }
        Assert.NotNull(folder);
        return File.ReadAllBytes(Path.Combine(folder.FullName, "assets", "board", "sounds", name));
    }

    static short[] Samples(byte[] wave) =>
        [.. Enumerable.Range(0, (wave.Length - 44) / 2).Select(i => BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44 + i * 2)))];

    [Theory]
    [InlineData("tock.wav", 100)]
    [InlineData("pop.wav", 70)]
    public void Each_is_a_short_mono_16_bit_pcm_wave(string name, int ms)
    {
        var wave = Wave(name);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal(wave.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(4)));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wave, 8, 8));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(22)));
        Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24)));
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34)));
        Assert.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));
        Assert.Equal(wave.Length - 44, BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40)));
        Assert.Equal(44100 * ms / 1000, Samples(wave).Length);
    }

    [Theory]
    [InlineData("tock.wav")]
    [InlineData("pop.wav")]
    public void Each_is_present_but_never_loud_and_starts_and_ends_silent(string name)
    {
        var samples = Samples(Wave(name));
        var peak = samples.Max(s => Math.Abs((int)s)) / 32767.0;
        Assert.InRange(peak, 0.03, 0.2);
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1]);
        // The sound is at its start: all of it in the first 60 ms.
        Assert.True(samples.Skip(44100 * 60 / 1000).All(s => Math.Abs((int)s) < 100));
    }
}
