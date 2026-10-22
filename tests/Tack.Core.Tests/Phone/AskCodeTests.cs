using Tack.Core.Phone;

namespace Tack.Core.Tests.Phone;

public class AskCodeTests
{
    [Fact]
    public void A_code_is_always_four_digits()
    {
        for (var i = 0; i < 1000; i++)
        {
            var code = AskCode.Fresh();
            Assert.Equal(4, code.Length);
            Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));
        }
    }
}
