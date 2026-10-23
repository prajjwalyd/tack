using System.ComponentModel;
using System.Text.Json;
using Tack.Core.Updates;

namespace Tack.Core.Tests.Updates;

public class UpdateFailuresTests
{
    [Fact]
    public void Failures_are_sorted_into_the_kinds_told_apart()
    {
        Assert.Equal(UpdateFailure.Network, UpdateFailures.Classify(new HttpRequestException("no route")));
        Assert.Equal(UpdateFailure.Network, UpdateFailures.Classify(new TaskCanceledException()));
        Assert.Equal(UpdateFailure.Network, UpdateFailures.Classify(new HttpIOException(HttpRequestError.ResponseEnded)));
        Assert.Equal(UpdateFailure.Server, UpdateFailures.Classify(new UpdateFailureException(UpdateFailure.Server, "the server answered 404")));
        Assert.Equal(UpdateFailure.Server, UpdateFailures.Classify(new JsonException()));
        Assert.Equal(UpdateFailure.Signature, UpdateFailures.Classify(new UpdateFailureException(UpdateFailure.Signature, "signed for 0.1.0")));
        Assert.Equal(UpdateFailure.Local, UpdateFailures.Classify(new IOException("disk full")));
        Assert.Equal(UpdateFailure.Local, UpdateFailures.Classify(new UnauthorizedAccessException()));
        Assert.Equal(UpdateFailure.Local, UpdateFailures.Classify(new Win32Exception(2)));
        Assert.Equal(UpdateFailure.Other, UpdateFailures.Classify(new InvalidOperationException()));
    }

    [Fact]
    public void Each_kind_has_its_own_fixed_words()
    {
        var words = Enum.GetValues<UpdateFailure>().Select(UpdateFailures.Words).ToList();
        Assert.Equal(5, words.Distinct().Count());
        Assert.Equal(UpdateFailures.Signature, UpdateFailures.Words(UpdateFailure.Signature));
    }

    [Fact]
    public void What_the_person_reads_never_carries_the_error_text()
    {
        Exception[] errors =
        [
            new HttpRequestException("https://example.invalid/?token=secret"),
            new UpdateFailureException(UpdateFailure.Server, "https://example.invalid/?token=secret"),
            new IOException(@"C:\Users\secret\AppData\Local\Temp"),
            new InvalidOperationException("secret"),
        ];
        foreach (var error in errors)
        {
            var told = UpdateFailures.Words(UpdateFailures.Classify(error));
            Assert.DoesNotContain("example", told, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", told, StringComparison.Ordinal);
        }
    }
}
