namespace Tack.Core.Phone.Qr;

/// <summary>
/// How hard a masked code is to read: the lower, the better. These are the
/// standard's four rules as Tack 0.2 scored them, so the same mask wins.
/// </summary>
internal static class QrPenalty
{
    static readonly bool[] FinderLike = [true, false, true, true, true, false, true];

    /// <summary>The total penalty of <paramref name="canvas"/> as it is drawn now.</summary>
    public static int Score(QrCanvas canvas) =>
        Runs(canvas, true) + Runs(canvas, false) + Blocks(canvas)
        + Finders(canvas, true) + Finders(canvas, false) + Balance(canvas);

    /// <summary>A run of five or more modules of one colour in a line scores its length less two.</summary>
    static int Runs(QrCanvas canvas, bool rows)
    {
        var score = 0;
        for (var line = 0; line < canvas.Size; line++)
        {
            var run = 1;
            for (var i = 1; i <= canvas.Size; i++)
            {
                if (i < canvas.Size && At(canvas, rows, line, i) == At(canvas, rows, line, i - 1))
                {
                    run++;
                    continue;
                }
                if (run >= 5)
                {
                    score += run - 2;
                }
                run = 1;
            }
        }
        return score;
    }

    /// <summary>Each 2×2 square of one colour, overlaps counted, scores 3.</summary>
    static int Blocks(QrCanvas canvas)
    {
        var score = 0;
        for (var y = 0; y < canvas.Size - 1; y++)
        {
            for (var x = 0; x < canvas.Size - 1; x++)
            {
                var dark = canvas.IsDark(x, y);
                if (canvas.IsDark(x + 1, y) == dark && canvas.IsDark(x, y + 1) == dark
                    && canvas.IsDark(x + 1, y + 1) == dark)
                {
                    score += 3;
                }
            }
        }
        return score;
    }

    /// <summary>
    /// Dark, light, three dark, light, dark in a line scores 40 when the four
    /// modules on either side of it are all light (or off the code). The three
    /// finders score the same for every mask, so they are left in.
    /// </summary>
    static int Finders(QrCanvas canvas, bool rows)
    {
        var score = 0;
        for (var line = 0; line < canvas.Size; line++)
        {
            for (var start = 0; start + 7 <= canvas.Size; start++)
            {
                if (Matches(canvas, rows, line, start)
                    && (!AnyDark(canvas, rows, line, start - 4, start)
                        || !AnyDark(canvas, rows, line, start + 7, start + 11)))
                {
                    score += 40;
                }
            }
        }
        return score;
    }

    /// <summary>The share of dark modules, in half percents, away from half.</summary>
    static int Balance(QrCanvas canvas)
    {
        var dark = 0;
        for (var y = 0; y < canvas.Size; y++)
        {
            for (var x = 0; x < canvas.Size; x++)
            {
                dark += canvas.IsDark(x, y) ? 1 : 0;
            }
        }
        return Math.Abs((dark * 200 / (canvas.Size * canvas.Size)) - 100);
    }

    static bool Matches(QrCanvas canvas, bool rows, int line, int start)
    {
        for (var k = 0; k < FinderLike.Length; k++)
        {
            if (At(canvas, rows, line, start + k) != FinderLike[k])
            {
                return false;
            }
        }
        return true;
    }

    static bool AnyDark(QrCanvas canvas, bool rows, int line, int from, int to)
    {
        for (var i = Math.Max(from, 0); i < Math.Min(to, canvas.Size); i++)
        {
            if (At(canvas, rows, line, i))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The module at <paramref name="i"/> along row or column <paramref name="line"/>.</summary>
    static bool At(QrCanvas canvas, bool rows, int line, int i) => rows ? canvas.IsDark(i, line) : canvas.IsDark(line, i);
}
