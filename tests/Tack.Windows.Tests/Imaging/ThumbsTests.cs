using Tack.Windows.Imaging;
using static Tack.Windows.Tests.Imaging.TestPictures;

namespace Tack.Windows.Tests.Imaging;

public sealed class ThumbsTests
{
    [Fact]
    public async Task Thumbnails_keep_the_original_size_and_shrink_the_picture()
    {
        var thumb = await Thumbs.FromPictureAsync(Solid(1200, 600, 10, 20, 30));

        Assert.Equal((1200, 600), (thumb.Width, thumb.Height));
        Assert.Equal((360, 180), await SizeOf(thumb.Jpeg));
    }

    [Fact]
    public async Task A_flight_picture_is_capped_and_its_thumbnail_keeps_the_original_size()
    {
        var picture = Solid(3200, 1600, 200, 100, 50);

        var flight = await Thumbs.ForFlightAsync(picture, picture.Hash());

        Assert.True(IsJpeg(flight.Jpeg));
        Assert.Equal((1600, 800), await SizeOf(flight.Jpeg));
        Assert.Equal((3200, 1600), (flight.Thumb.Width, flight.Thumb.Height));
        // The full picture's hash, not the flight picture's.
        Assert.Equal((await Thumbs.FromPictureAsync(picture)).Hash, flight.Thumb.Hash);
    }

    [Fact]
    public async Task A_small_picture_keeps_its_size()
    {
        var thumb = await Thumbs.FromPictureAsync(Solid(100, 50, 1, 2, 3));

        Assert.Equal((100, 50), await SizeOf(thumb.Jpeg));
    }

    [Fact]
    public async Task Transparency_goes_on_white_paper()
    {
        var thumb = await Thumbs.FromPictureAsync(Solid(8, 8, 0, 0, 0, a: 0));

        var pixels = (await PictureDecoder.DecodeAsync(thumb.Jpeg)).Bgra;

        Assert.All(pixels, channel => Assert.True(channel >= 250));
    }

    [Fact]
    public async Task A_file_thumbnail_carries_the_full_picture_hash()
    {
        using var scratch = new Scratch("thumbs");
        var picture = Busy(640, 360);
        var path = scratch.File("Screenshot 2026-10-22 101500.png");
        await File.WriteAllBytesAsync(path, await PictureEncoder.PngAsync(picture), TestContext.Current.CancellationToken);

        var thumb = await Thumbs.MakeAsync(path);

        Assert.Equal((640, 360, picture.Hash()), (thumb.Width, thumb.Height, thumb.Hash));
    }

    [Fact]
    public async Task A_screenshot_still_being_written_is_waited_for()
    {
        using var scratch = new Scratch("patient");
        var path = scratch.File("Screenshot 2026-10-22 101501.png");
        var png = await PictureEncoder.PngAsync(Busy(64, 48));
        // Snipping Tool creates the file first and writes it a moment later.
        await File.WriteAllBytesAsync(path, [], TestContext.Current.CancellationToken);
        var writing = Task.Run(async () =>
        {
            await Task.Delay(400, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(path, png, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        var thumb = await Thumbs.MakePatientlyAsync(path, TimeSpan.FromSeconds(5));

        await writing;
        Assert.Equal((64, 48), (thumb.Width, thumb.Height));
    }

    [Fact]
    public async Task A_screenshot_that_is_gone_is_given_up_at_once()
    {
        using var scratch = new Scratch("gone");
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<IOException>(() => Thumbs.MakePatientlyAsync(scratch.File("gone.png"), TimeSpan.FromSeconds(5)));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }
}
