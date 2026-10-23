using System.Text;
using Tack.Core.Updates;
using Tack.Core.Updates.Signing;
using static Tack.Core.Tests.Updates.Signing.MinisignSamples;

namespace Tack.Core.Tests.Updates.Signing;

public class MinisignTests
{
    [Fact]
    public void A_legacy_signature_verifies_and_gives_its_trusted_comment()
    {
        Assert.Equal("timestamp:1555779966\tfile:test", Minisign.Verify(Pub, Legacy, Content()));
        Assert.Null(Minisign.Verify(Pub, Legacy, Content("Test")));
    }

    [Fact]
    public void A_prehashed_signature_verifies_and_gives_its_trusted_comment()
    {
        Assert.Equal("timestamp:1556193335\tfile:test", Minisign.Verify(Pub, Prehashed, Content()));
        Assert.Equal(
            "timestamp:1633700835\tfile:test\tprehashed",
            Minisign.Verify(Pub, Wrap(PrehashedSigText2), Content()));
    }

    [Fact]
    public void Content_read_a_byte_at_a_time_verifies()
    {
        Assert.NotNull(Minisign.Verify(Pub, Prehashed, new TrickleStream("test"u8.ToArray())));
        Assert.NotNull(Minisign.Verify(Pub, Legacy, new TrickleStream("test"u8.ToArray())));
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("test\n")]
    [InlineData("tes")]
    [InlineData("")]
    public void Changed_content_fails(string content)
    {
        Assert.Null(Minisign.Verify(Pub, Prehashed, Content(content)));
        Assert.Null(Minisign.Verify(Pub, Legacy, Content(content)));
    }

    [Fact]
    public void Another_key_fails()
    {
        // Tack's own key has another key id.
        Assert.Null(Minisign.Verify(UpdateTrust.PublicKey, Prehashed, Content()));

        // The same key id with another key: the signature itself fails.
        Assert.Null(Minisign.Verify(Wrap(PubText.Replace(KeyLine, Line(KeyLine, b => b[20] ^= 1))), Prehashed, Content()));
    }

    [Fact]
    public void A_changed_trusted_comment_fails()
    {
        Assert.Null(Minisign.Verify(Pub, Wrap(PrehashedSigText.Replace("file:test", "file:tesT")), Content()));
        Assert.Null(Minisign.Verify(Pub, Wrap(LegacySigText.Replace("1555779966", "1555779967")), Content()));
        Assert.Null(Minisign.Verify(
            Pub, Wrap(PrehashedSigText.Replace("file:test", "file:test\tversion:9.9.9")), Content()));
    }

    [Fact]
    public void A_changed_signature_or_global_signature_fails()
    {
        var lines = PrehashedSigText.Split('\n');

        Assert.Null(Minisign.Verify(Pub, WithLine(lines, 1, Line(lines[1], b => b[40] ^= 0x10)), Content()));
        Assert.Null(Minisign.Verify(Pub, WithLine(lines, 3, Line(lines[3], b => b[0] ^= 0x01)), Content()));
    }

    [Theory]
    [InlineData((byte)'E', (byte)'x')]
    [InlineData((byte)'e', (byte)'D')]
    [InlineData((byte)'e', (byte)'d')]
    [InlineData((byte)0, (byte)0)]
    public void Another_algorithm_fails(byte first, byte second)
    {
        var lines = PrehashedSigText.Split('\n');
        var signature = WithLine(lines, 1, Line(lines[1], b => (b[0], b[1]) = (first, second)));
        var key = Wrap(PubText.Replace(KeyLine, Line(KeyLine, b => (b[0], b[1]) = (first, second))));

        Assert.Null(Minisign.Verify(Pub, signature, Content()));
        Assert.Null(Minisign.Verify(key, Prehashed, Content()));
    }

    [Fact]
    public void A_key_saying_ED_is_taken_as_tack_0_2_took_it()
    {
        var key = Wrap(PubText.Replace(KeyLine, Line(KeyLine, b => b[1] = (byte)'D')));

        Assert.NotNull(Minisign.Verify(key, Prehashed, Content()));
    }

    [Fact]
    public void A_legacy_signature_relabelled_prehashed_fails_and_the_other_way_round()
    {
        var prehashed = PrehashedSigText.Split('\n');
        var legacy = LegacySigText.Split('\n');

        Assert.Null(Minisign.Verify(Pub, WithLine(legacy, 1, Line(legacy[1], b => b[1] = (byte)'D')), Content()));
        Assert.Null(Minisign.Verify(Pub, WithLine(prehashed, 1, Line(prehashed[1], b => b[1] = (byte)'d')), Content()));
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(1, 1)]
    [InlineData(3, -1)]
    [InlineData(3, 1)]
    public void A_signature_line_of_the_wrong_length_fails(int line, int change)
    {
        var lines = PrehashedSigText.Split('\n');
        var bytes = Convert.FromBase64String(lines[line]);
        var resized = change < 0 ? bytes[..^1] : [.. bytes, 0];

        Assert.Null(Minisign.Verify(Pub, WithLine(lines, line, Convert.ToBase64String(resized)), Content()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void A_key_of_the_wrong_length_fails(int change)
    {
        var bytes = Convert.FromBase64String(KeyLine);
        var resized = change < 0 ? bytes[..^1] : [.. bytes, 0];

        Assert.Null(Minisign.Verify(Wrap(PubText.Replace(KeyLine, Convert.ToBase64String(resized))), Prehashed, Content()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("dGVzdA")]
    [InlineData("dGVzdA=")]
    [InlineData("dGVzdB==")]
    [InlineData("////")]
    public void Garbage_base64_fails(string garbage)
    {
        Assert.Null(Minisign.Verify(garbage, Prehashed, Content()));
        Assert.Null(Minisign.Verify(Pub, garbage, Content()));
    }

    [Fact]
    public void Base64_that_is_almost_right_fails()
    {
        // Whitespace, a stray bit in the last character, invalid UTF-8 inside.
        Assert.Null(Minisign.Verify(Pub, Prehashed.Insert(8, "\n"), Content()));
        Assert.Null(Minisign.Verify(Pub, " " + Prehashed, Content()));
        Assert.Null(Minisign.Verify(Pub, Wrap(LegacySigText.Replace("SSbXxwA=", "SSbXxwB=")), Content()));
        Assert.Null(Minisign.Verify(Pub, Convert.ToBase64String([.. Encoding.UTF8.GetBytes(PrehashedSigText), 0xFF]), Content()));
    }

    [Fact]
    public void A_signature_without_its_four_lines_fails()
    {
        var lines = PrehashedSigText.Split('\n');

        Assert.Null(Minisign.Verify(Pub, Wrap(string.Join('\n', lines[..3])), Content()));
        Assert.Null(Minisign.Verify(Pub, WithLine(lines, 2, lines[2].Replace("trusted comment: ", "trusted comment:")), Content()));
        Assert.Null(Minisign.Verify(Pub, WithLine(lines, 2, lines[2].Replace("trusted", "untrusted")), Content()));
        Assert.Null(Minisign.Verify(Wrap(KeyLine), Prehashed, Content()));
    }

    [Fact]
    public void Windows_line_endings_and_a_final_newline_are_read_as_tack_0_2_read_them()
    {
        Assert.NotNull(Minisign.Verify(Wrap(PubText.Replace("\n", "\r\n") + "\r\n"), Wrap(PrehashedSigText.Replace("\n", "\r\n") + "\r\n"), Content()));
        Assert.NotNull(Minisign.Verify(Pub, Wrap(PrehashedSigText + "\nanything after the fourth line"), Content()));
    }

    [Fact]
    public void Null_arguments_give_null()
    {
        Assert.Null(Minisign.Verify(null!, Prehashed, Content()));
        Assert.Null(Minisign.Verify(Pub, null!, Content()));
        Assert.Null(Minisign.Verify(Pub, Prehashed, null!));
    }

    /// <summary>A base64 line with its bytes changed by <paramref name="change"/>, encoded again.</summary>
    static string Line(string base64, Action<byte[]> change)
    {
        var bytes = Convert.FromBase64String(base64);
        change(bytes);
        return Convert.ToBase64String(bytes);
    }

    static string WithLine(string[] lines, int index, string replacement)
    {
        var copy = (string[])lines.Clone();
        copy[index] = replacement;
        return Wrap(string.Join('\n', copy));
    }
}
