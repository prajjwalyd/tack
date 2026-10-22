using System.Net;
using Tack.Core.NetBird;
using Tack.Core.Phone.Serving;

namespace Tack.Core.Tests.Phone.Serving;

public class PeerBookTests
{
    static readonly IPAddress MyIp = IPAddress.Parse("100.90.0.1");
    static readonly Peer Pixel = new(IPAddress.Parse("100.90.0.2"), "pixel.netbird.cloud", "cGl4ZWw=");
    static readonly Peer Laptop = new(IPAddress.Parse("100.90.0.3"), "laptop.netbird.cloud", "bGFwdG9w");
    static readonly NetBirdStatus Me = new(MyIp, "my-pc.netbird.cloud", [Pixel]);
    static readonly NetBirdStatus WithLaptop = Me with { Peers = [Pixel, Laptop] };

    readonly ManualTime _time = new();
    int _looks;

    /// <summary>What NetBird says when asked.</summary>
    NetBirdStatus? _says = WithLaptop;

    PeerBook Book() => new(Me, () =>
    {
        _looks++;
        return Task.FromResult(_says);
    }, _time);

    [Fact]
    public async Task A_known_peer_is_found_without_asking_netbird()
    {
        var book = Book();
        _time.Advance(TimeSpan.FromHours(1));

        var match = await book.FindAsync(Pixel.Ip, fresh: false);

        Assert.Equal(new PeerMatch(Me, Pixel), match);
        Assert.Equal(0, _looks);
    }

    [Fact]
    public async Task A_fresh_request_asks_again_once_the_list_is_ten_seconds_old()
    {
        var book = Book();

        _time.Advance(PeerBook.FreshFor - TimeSpan.FromTicks(1));
        await book.FindAsync(Pixel.Ip, fresh: true);
        Assert.Equal(0, _looks);

        _time.Advance(TimeSpan.FromTicks(1));
        var match = await book.FindAsync(Pixel.Ip, fresh: true);
        Assert.Equal(1, _looks);
        Assert.Same(WithLaptop, match!.Me);

        // The look starts the age again.
        _time.Advance(PeerBook.FreshFor - TimeSpan.FromTicks(1));
        await book.FindAsync(Pixel.Ip, fresh: true);
        Assert.Equal(1, _looks);
    }

    [Fact]
    public async Task An_unknown_address_asks_again_at_most_every_three_seconds()
    {
        var book = Book();

        _time.Advance(PeerBook.UnknownRecheck - TimeSpan.FromTicks(1));
        Assert.Null(await book.FindAsync(Laptop.Ip, fresh: false));
        Assert.Equal(0, _looks);

        _time.Advance(TimeSpan.FromTicks(1));
        var match = await book.FindAsync(Laptop.Ip, fresh: false);
        Assert.Equal(new PeerMatch(WithLaptop, Laptop), match);
        Assert.Equal(1, _looks);
        Assert.Same(WithLaptop, book.Status);
    }

    [Fact]
    public async Task A_look_that_finds_nothing_still_starts_the_age_again()
    {
        _says = null;
        var book = Book();
        var stranger = IPAddress.Parse("100.90.9.9");

        _time.Advance(PeerBook.UnknownRecheck);
        Assert.Null(await book.FindAsync(stranger, fresh: true));
        _time.Advance(PeerBook.UnknownRecheck - TimeSpan.FromTicks(1));
        Assert.Null(await book.FindAsync(stranger, fresh: true));
        Assert.Equal(1, _looks);

        _time.Advance(TimeSpan.FromTicks(1));
        Assert.Null(await book.FindAsync(stranger, fresh: true));
        Assert.Equal(2, _looks);
        Assert.Same(Me, book.Status);
    }

    [Fact]
    public async Task A_status_at_another_address_is_not_taken()
    {
        _says = WithLaptop with { Ip = IPAddress.Parse("100.90.7.7") };
        var book = Book();

        _time.Advance(PeerBook.UnknownRecheck);
        Assert.Null(await book.FindAsync(Laptop.Ip, fresh: false));
        Assert.Equal(1, _looks);
        Assert.Same(Me, book.Status);

        // It still counts as a look.
        _time.Advance(PeerBook.UnknownRecheck - TimeSpan.FromTicks(1));
        Assert.Null(await book.FindAsync(Laptop.Ip, fresh: false));
        Assert.Equal(1, _looks);
    }

    [Fact]
    public async Task Only_one_caller_asks_netbird_at_a_time()
    {
        var answer = new TaskCompletionSource<NetBirdStatus?>();
        var looks = 0;
        var book = new PeerBook(Me, () =>
        {
            Interlocked.Increment(ref looks);
            return answer.Task;
        }, _time);
        _time.Advance(PeerBook.FreshFor);

        var first = book.FindAsync(Laptop.Ip, fresh: true);
        var second = await book.FindAsync(Laptop.Ip, fresh: true);
        var third = await book.FindAsync(Pixel.Ip, fresh: true);

        Assert.False(first.IsCompleted);
        Assert.Null(second);
        Assert.Equal(new PeerMatch(Me, Pixel), third);
        Assert.Equal(1, looks);

        answer.SetResult(WithLaptop);
        Assert.Equal(new PeerMatch(WithLaptop, Laptop), await first);

        // Once it is done, the next stale request may ask again.
        _time.Advance(PeerBook.FreshFor);
        answer = new TaskCompletionSource<NetBirdStatus?>();
        answer.SetResult(Me);
        Assert.Null(await book.FindAsync(Laptop.Ip, fresh: true));
        Assert.Equal(2, looks);
    }

    [Fact]
    public async Task A_look_that_fails_lets_the_next_one_ask()
    {
        var calls = 0;
        var book = new PeerBook(Me, () =>
        {
            calls++;
            return calls == 1 ? throw new InvalidOperationException("netbird") : Task.FromResult<NetBirdStatus?>(WithLaptop);
        }, _time);
        _time.Advance(PeerBook.UnknownRecheck);

        await Assert.ThrowsAsync<InvalidOperationException>(() => book.FindAsync(Laptop.Ip, fresh: false));
        _time.Advance(PeerBook.UnknownRecheck);

        Assert.NotNull(await book.FindAsync(Laptop.Ip, fresh: false));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task An_update_from_the_watcher_is_taken_only_at_this_address()
    {
        var book = Book();

        book.Update(WithLaptop with { Ip = IPAddress.Parse("100.90.7.7") });
        Assert.Same(Me, book.Status);

        _time.Advance(PeerBook.FreshFor);
        book.Update(WithLaptop);
        Assert.Same(WithLaptop, book.Status);

        // An update is a fresh look: no need to ask NetBird.
        Assert.Equal(new PeerMatch(WithLaptop, Laptop), await book.FindAsync(Laptop.Ip, fresh: true));
        Assert.Equal(0, _looks);
    }
}
