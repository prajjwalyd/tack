using System.Text;
using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

/// <summary>
/// The golden codes were made by Tack 0.2's encoder, one row of modules per
/// line, 1 for dark. Matching them means a phone sees the same code as before.
/// </summary>
public class QrEncoderTests
{
    /// <summary>Bytes, then the port as digits: version 3.</summary>
    const string Address = """
        11111110011000100111001111111
        10000010110101000111101000001
        10111010001100010011101011101
        10111010010101001011001011101
        10111010110111001111001011101
        10000010010000111011001000001
        11111110101010101010101111111
        00000000010110010110100000000
        10101010000100001101100010010
        01010101101101011110011101001
        01001111001000111000110100111
        11100000011101101111111101010
        11100111000110110100111001011
        01111000110010110100111001101
        01011011010101000000110010011
        11000100011110010100011101010
        00100010010110001110011100011
        01110101110101011010111000101
        10010111001110111110011111011
        01101000000001101101000011010
        10101010001100111011111110000
        00000000100100110111100010111
        11111110001001001011101011011
        10000010001110010100100010001
        10111010110000000010111110000
        10111010011010001111100110100
        10111010111011000110110011101
        10000010010101000111100000010
        11111110101001001011101001011
        """;

    /// <summary>Version 1.</summary>
    const string Short = """
        111111101010101111111
        100000101001001000001
        101110101100001011101
        101110100001101011101
        101110101000101011101
        100000100111101000001
        111111101010101111111
        000000000101100000000
        100111111010110010111
        001100011000101010100
        000011110100011010111
        101000000111000000100
        011000100110001001001
        000000001101100100100
        111111101110111111000
        100000101101110111101
        101110101011101101011
        101110101101100011000
        101110100110001000011
        100000100010011010111
        111111101111000000100
        """;

    /// <summary>Bytes, then the port as digits: version 2.</summary>
    const string IpAddress = """
        1111111011010110001111111
        1000001011010110001000001
        1011101000010101101011101
        1011101010100001101011101
        1011101000100001001011101
        1000001001000100101000001
        1111111010101010101111111
        0000000010101011100000000
        1011011101011101101001011
        0110110110100010010000010
        1010001011110110110010000
        1010110100001110100101100
        1110001011110001101110111
        0111010010010011011010001
        0100111011001000111000110
        1011000110001010100001001
        0000111001010101111111111
        0000000011101110100010001
        1111111010111000101011111
        1000001010001010100010011
        1011101000111100111110000
        1011101010001101001110111
        1011101010111100001011110
        1000001001000001001010100
        1111111010001011011111111
        """;

    [Fact]
    public void The_board_address_is_the_code_Tack_0_2_showed() =>
        AssertGolden(Address, "http://my-pc.netbird.cloud:7717");

    [Fact]
    public void A_short_text_fits_version_1() => AssertGolden(Short, "tack");

    [Fact]
    public void An_address_by_number_is_the_code_Tack_0_2_showed() =>
        AssertGolden(IpAddress, "http://100.90.1.2:7717");

    [Fact]
    public void A_long_address_takes_version_6() =>
        AssertGolden(
            QrEncoderGoldens.LongAddress,
            "http://a-rather-long-machine-name.netbird.cloud:7717/some/longer/path/to/version/five");

    [Fact]
    public void Two_hundred_bytes_take_version_10_with_its_version_information() =>
        AssertGolden(QrEncoderGoldens.TwoHundredBytes, QrEncoderGoldens.TwoHundredBytesText);

    [Fact]
    public void Accented_letters_are_UTF_8_bytes() =>
        AssertGolden(QrEncoderGoldens.Accented, "http://café-pc.netbird.cloud:7717/über");

    [Fact]
    public void Japanese_is_UTF_8_bytes_too() =>
        AssertGolden(QrEncoderGoldens.Japanese, "http://pc.netbird.cloud:7717/ピン");

    [Fact]
    public void Empty_text_is_an_empty_version_1_code()
    {
        Assert.Equal(21, QrEncoder.Encode("")?.Size);
    }

    [Fact]
    public void Version_40_holds_2331_bytes_and_no_more()
    {
        Assert.Equal(177, QrEncoder.Encode(new string('a', 2331))?.Size);
        Assert.Null(QrEncoder.Encode(new string('a', 2332)));
    }

    [Fact]
    public void Digits_are_packed_tighter_than_bytes()
    {
        Assert.Equal(177, QrEncoder.Encode(new string('7', 5596))?.Size);
        Assert.Null(QrEncoder.Encode(new string('7', 5597)));
    }

    static void AssertGolden(string golden, string text)
    {
        var code = QrEncoder.Encode(text);
        Assert.NotNull(code);
        var rows = new StringBuilder();
        for (var y = 0; y < code.Size; y++)
        {
            for (var x = 0; x < code.Size; x++)
            {
                rows.Append(code.IsDark(x, y) ? '1' : '0');
            }
            rows.Append('\n');
        }
        Assert.Equal(golden.ReplaceLineEndings("\n") + "\n", rows.ToString());
    }
}
