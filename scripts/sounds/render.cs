// Renders the board's two sounds into src/Tack.App/Assets/Sounds/: the tock of a pin
// going into cork and the pop of one coming out, synthesised as Tack 0.2 did
// live (sine sweeps and band-passed noise under exponential envelopes, through
// the same biquad filters), at its master level. The noise is seeded, so a run
// writes the same bytes every time.
//
//   dotnet run scripts/sounds/render.cs

using System.Runtime.CompilerServices;

const int Rate = 44100;
const double Master = 0.18;

var noise = Synth.Noise((int)Math.Ceiling(Rate * 0.08), seed: 0x7ac4);
var folder = Path.GetFullPath(Path.Combine(Here(), "..", "..", "src", "Tack.App", "Assets", "Sounds"));
Directory.CreateDirectory(folder);
Write(Path.Combine(folder, "tock.wav"), Tock());
Write(Path.Combine(folder, "pop.wav"), Pop());

// Pin into cork: a low thump dropping in pitch as the pin seats, the cork's
// body (noise through a narrow, fast-dying resonance) and a touch of contact
// noise, all rounded off by one low-pass.
double[] Tock()
{
    var n = (int)(Rate * 0.1);
    var press = Synth.Times(Synth.Sweep(Rate, n, 190, 112, 0.045, stop: 0.09), Synth.Envelope(Rate, n, 0.85, 0.003, 0.06));
    var body = Synth.Times(
        Synth.BandPass(Rate, Synth.Gate(Rate, noise, n, 0.07), 420, 7), Synth.Envelope(Rate, n, 1.4, 0.002, 0.05));
    var contact = Synth.Times(
        Synth.BandPass(Rate, Synth.Gate(Rate, noise, n, 0.03), 1100, 0.9), Synth.Envelope(Rate, n, 0.18, 0.001, 0.014));
    var mix = Synth.LowPass(Rate, Synth.Plus(press, body, contact), 1600, qDb: 0.5);
    return Synth.Scaled(mix, Master * 0.85);
}

// Pin out of cork: a small, dry upward tick.
double[] Pop()
{
    var n = (int)(Rate * 0.07);
    var tick = Synth.Times(Synth.Sweep(Rate, n, 520, 880, 0.035, stop: 0.06), Synth.Envelope(Rate, n, 0.6, 0.002, 0.04));
    var dry = Synth.Times(
        Synth.BandPass(Rate, Synth.Gate(Rate, noise, n, 0.025), 1800, 1.4), Synth.Envelope(Rate, n, 0.25, 0.001, 0.012));
    return Synth.Scaled(Synth.Plus(tick, dry), Master * 0.7);
}

// 16-bit mono PCM, the last 2 ms faded so the end never clicks.
static void Write(string path, double[] samples)
{
    var fade = Rate / 500;
    using var file = new BinaryWriter(File.Create(path));
    var bytes = samples.Length * 2;
    file.Write("RIFF"u8);
    file.Write(36 + bytes);
    file.Write("WAVEfmt "u8);
    file.Write(16);
    file.Write((short)1);
    file.Write((short)1);
    file.Write(Rate);
    file.Write(Rate * 2);
    file.Write((short)2);
    file.Write((short)16);
    file.Write("data"u8);
    file.Write(bytes);
    for (var i = 0; i < samples.Length; i++)
    {
        var left = samples.Length - 1 - i;
        var v = left < fade ? samples[i] * left / fade : samples[i];
        file.Write((short)Math.Round(Math.Clamp(v, -1, 1) * 32767));
    }
    Console.WriteLine($"{path}: {12 + 24 + 8 + bytes} bytes");
}

static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

/// <summary>The few audio graph parts the two sounds use, one buffer at a time.</summary>
static class Synth
{
    const double Floor = 0.0001;

    /// <summary>White noise in -1 to 1 from a fixed seed (xorshift32).</summary>
    public static double[] Noise(int length, uint seed)
    {
        var buffer = new double[length];
        var state = seed;
        for (var i = 0; i < length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            buffer[i] = state / (double)uint.MaxValue * 2 - 1;
        }
        return buffer;
    }

    /// <summary>The noise buffer played once from the start, silent after <paramref name="stop"/> seconds.</summary>
    public static double[] Gate(int rate, double[] noise, int length, double stop)
    {
        var buffer = new double[length];
        var end = Math.Min(Math.Min(length, noise.Length), (int)Math.Round(stop * rate));
        Array.Copy(noise, buffer, end);
        return buffer;
    }

    /// <summary>A sine from phase 0 whose pitch moves exponentially from <paramref name="from"/> to <paramref name="to"/> Hz, then holds; silent after <paramref name="stop"/>.</summary>
    public static double[] Sweep(int rate, int length, double from, double to, double seconds, double stop)
    {
        var buffer = new double[length];
        var phase = 0.0;
        for (var i = 0; i < length && i < stop * rate; i++)
        {
            var t = i / (double)rate;
            buffer[i] = Math.Sin(phase);
            var f = t < seconds ? from * Math.Pow(to / from, t / seconds) : to;
            phase += 2 * Math.PI * f / rate;
        }
        return buffer;
    }

    /// <summary>A gain from almost nothing up to <paramref name="peak"/> and back down, both exponentially.</summary>
    public static double[] Envelope(int rate, int length, double peak, double attack, double decay)
    {
        var buffer = new double[length];
        for (var i = 0; i < length; i++)
        {
            var t = i / (double)rate;
            buffer[i] = t < attack ? Floor * Math.Pow(peak / Floor, t / attack)
                : t < attack + decay ? peak * Math.Pow(Floor / peak, (t - attack) / decay)
                : Floor;
        }
        return buffer;
    }

    /// <summary>A band-pass biquad with a peak gain of 1; Q is linear.</summary>
    public static double[] BandPass(int rate, double[] x, double hz, double q)
    {
        var w = 2 * Math.PI * hz / rate;
        var alpha = Math.Sin(w) / (2 * q);
        return Biquad(x, alpha, 0, -alpha, 1 + alpha, -2 * Math.Cos(w), 1 - alpha);
    }

    /// <summary>A low-pass biquad; its Q is a resonance in dB.</summary>
    public static double[] LowPass(int rate, double[] x, double hz, double qDb)
    {
        var w = 2 * Math.PI * hz / rate;
        var alpha = Math.Sin(w) / (2 * Math.Pow(10, qDb / 20));
        var c = Math.Cos(w);
        return Biquad(x, (1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
    }

    public static double[] Times(double[] a, double[] b) => [.. a.Select((v, i) => v * b[i])];

    public static double[] Plus(params double[][] layers) =>
        [.. Enumerable.Range(0, layers[0].Length).Select(i => layers.Sum(layer => layer[i]))];

    public static double[] Scaled(double[] a, double k) => [.. a.Select(v => v * k)];

    static double[] Biquad(double[] x, double b0, double b1, double b2, double a0, double a1, double a2)
    {
        var y = new double[x.Length];
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var i = 0; i < x.Length; i++)
        {
            y[i] = (b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            (x2, x1, y2, y1) = (x1, x[i], y1, y[i]);
        }
        return y;
    }
}
