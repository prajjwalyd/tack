using Tack.Windows.Imaging;
using Tack.Windows.Shell;

namespace Tack.Windows.Tests.Shell;

/// <summary>Everything up to the drag loop itself, which needs a held mouse button and a real drop.</summary>
public sealed unsafe class DragOutTests
{
    [Fact]
    public void A_file_gives_a_data_object_that_takes_a_drag_image()
    {
        using var folder = new Scratch("shell");
        var file = folder.SmallFile("Screenshot 2026-01-02 030405.png");
        Sta.Run(() =>
        {
            var data = DragOut.DataObjectOf(file);
            try
            {
                Assert.True(data != null);
                Assert.True(DragBitmap.TryAttach(data, new DragImage(2, 2, new byte[16])));
            }
            finally
            {
                data->Release();
            }
        });
    }

    [Fact]
    public void A_missing_file_has_no_data_object()
    {
        using var folder = new Scratch("shell");
        var missing = Path.Combine(folder.Path, "gone.png");
        Sta.Run(() => Assert.ThrowsAny<Exception>(() => DragOut.DataObjectOf(missing)));
    }

    [Theory]
    [InlineData(2, 2, 15)]
    [InlineData(0, 2, 0)]
    [InlineData(2, -2, 16)]
    public void An_image_whose_bytes_do_not_match_its_size_is_not_used(int width, int height, int bytes) =>
        Assert.False(new DragImage(width, height, new byte[bytes]).IsWhole);

    [Fact]
    public void The_effects_match_the_shell_values()
    {
        Assert.Equal(1, (int)DropEffect.Copy);
        Assert.Equal(2, (int)DropEffect.Move);
        Assert.Equal(4, (int)DropEffect.Link);
    }
}
