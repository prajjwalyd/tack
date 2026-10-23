using Tack.Core.Updates;

namespace Tack.Core.Tests.Updates;

public class HttpsRedirectsTests
{
    static readonly Uri s_feed = new(UpdateTrust.Feed);

    static UpdateFailure Refused(Func<Uri> next) => Assert.Throws<UpdateFailureException>(() => next()).Kind;

    [Fact]
    public void An_https_redirect_is_followed()
    {
        var cdn = new Uri("https://objects.githubusercontent.com/release/latest.json?sig=1");
        Assert.Equal(cdn, HttpsRedirects.Next(s_feed, cdn, 0));
        Assert.Equal(cdn, HttpsRedirects.Next(s_feed, cdn, HttpsRedirects.MaxRedirects - 1));
    }

    [Fact]
    public void A_relative_redirect_stays_on_the_same_https_server()
    {
        var next = HttpsRedirects.Next(s_feed, new Uri("/prajjwalyd/tack/releases/download/v0.3.1/latest.json", UriKind.Relative), 0);
        Assert.Equal("https://github.com/prajjwalyd/tack/releases/download/v0.3.1/latest.json", next.ToString());
    }

    [Fact]
    public void Every_hop_must_be_https()
    {
        Assert.Equal(UpdateFailure.Server, Refused(() => HttpsRedirects.Next(s_feed, new Uri("http://github.com/latest.json"), 0)));
        Assert.Equal(UpdateFailure.Server, Refused(() => HttpsRedirects.Next(s_feed, new Uri("ftp://github.com/latest.json"), 0)));
        Assert.Equal(UpdateFailure.Server, Refused(() => HttpsRedirects.Next(s_feed, new Uri("file:///C:/latest.json"), 0)));
        // A scheme-relative location keeps https, an "http:" one does not.
        Assert.True(HttpsRedirects.IsHttps(HttpsRedirects.Next(s_feed, new Uri("//cdn.example.test/x", UriKind.Relative), 0)));
    }

    [Fact]
    public void At_most_five_redirects_are_followed()
    {
        Assert.Equal(5, HttpsRedirects.MaxRedirects);
        Assert.Equal(UpdateFailure.Server, Refused(() => HttpsRedirects.Next(s_feed, s_feed, HttpsRedirects.MaxRedirects)));
    }

    [Fact]
    public void A_redirect_needs_a_location()
    {
        Assert.Equal(UpdateFailure.Server, Refused(() => HttpsRedirects.Next(s_feed, null, 0)));
    }

    [Theory]
    [InlineData(301, true)]
    [InlineData(302, true)]
    [InlineData(303, true)]
    [InlineData(307, true)]
    [InlineData(308, true)]
    [InlineData(200, false)]
    [InlineData(304, false)]
    [InlineData(300, false)]
    [InlineData(404, false)]
    public void Redirects_are_the_five_statuses_that_move_a_request(int status, bool redirect) =>
        Assert.Equal(redirect, HttpsRedirects.IsRedirect(status));

    [Fact]
    public void Only_absolute_https_addresses_pass()
    {
        Assert.True(HttpsRedirects.IsHttps(s_feed));
        Assert.True(HttpsRedirects.IsHttps(new Uri("HTTPS://GitHub.com/")));
        Assert.False(HttpsRedirects.IsHttps(new Uri("http://github.com/")));
        Assert.False(HttpsRedirects.IsHttps(new Uri("/latest.json", UriKind.Relative)));
    }
}
