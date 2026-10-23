using System.Text;
using Tack.Core.Updates;
using Tack.Core.Updates.Signing;
using static Tack.Core.Tests.Updates.Signing.MinisignSamples;

namespace Tack.Core.Tests.Updates;

/// <summary>The key every update must be signed with, and the check an installer goes through before it runs.</summary>
public class UpdateSignatureTests
{
    [Fact]
    public void The_shipped_key_reads_as_the_key_its_comment_names()
    {
        var text = StrictBase64.Decode(UpdateTrust.PublicKey);
        Assert.NotNull(text);

        var key = MinisignPublicKey.Parse(Encoding.UTF8.GetString(text));

        Assert.NotNull(key);
        // minisign prints a key id as hex, last byte first.
        Assert.Equal("9D20F5A0F231D92F", Convert.ToHexString([.. Enumerable.Reverse(key.KeyId)]));
    }

    [Fact]
    public void An_installer_signed_with_another_key_is_refused()
    {
        // A good signature, but not by Tack's key.
        Assert.NotNull(Minisign.Verify(Pub, Prehashed, Content()));

        var refused = Assert.Throws<UpdateFailureException>(() => UpdateSignature.Check(Prehashed, Content(), "0.3.0"));

        Assert.Equal(UpdateFailure.Signature, refused.Kind);
    }
}
