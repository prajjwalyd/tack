using Tack.Core.Appearance;
using Tack.Core.Storage.Json;

namespace Tack.Core.Tests.Storage;

/// <summary>
/// The base of the felt, frame, paper and speed converters: a saved choice
/// Tack does not know, or a value that is not even a string, reads as the default.
/// </summary>
public class LenientStringConverterTests
{
    [Theory]
    [InlineData("\"mauve\"")]
    [InlineData("\"\"")]
    [InlineData("null")]
    [InlineData("3")]
    [InlineData("true")]
    [InlineData("[1]")]
    [InlineData("{\"a\":1}")]
    public void A_saved_value_that_is_not_a_choice_reads_as_the_default(string json) =>
        Assert.Equal(Felt.Default, ConverterText.Read(new FeltJsonConverter(), json));
}
