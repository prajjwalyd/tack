using Tack.Core.Captures;
using Tack.Core.Pinboard.Input;

namespace Tack.Core.Tests.Pinboard.Input;

public class DropRulesTests
{
    static DroppedFile File(string name, string? type = null, long size = 1000) => new(name, type, size);

    [Fact]
    public void Files_a_link_or_text_can_be_pinned()
    {
        Assert.True(DropRules.Pinnable(files: true, link: false, text: false));
        Assert.True(DropRules.Pinnable(files: false, link: true, text: false));
        Assert.True(DropRules.Pinnable(files: false, link: false, text: true));
        Assert.False(DropRules.Pinnable(files: false, link: false, text: false));
    }

    [Theory]
    [InlineData("shot.png", null, true)]
    [InlineData("Shot.JPG", null, true)]
    [InlineData("photo.jpeg", "", true)]
    [InlineData("no-extension", "image/png", true)]
    [InlineData("camera", "image/jpeg", true)]
    [InlineData("picture.gif", "image/gif", false)]
    [InlineData("notes.txt", "text/plain", false)]
    [InlineData("png", null, false)]
    [InlineData("archive.png.zip", null, false)]
    public void An_image_is_a_PNG_or_JPEG_by_type_or_name(string name, string? type, bool image) =>
        Assert.Equal(image, DropRules.IsImage(File(name, type)));

    [Fact]
    public void Images_among_the_files_are_pinned_and_the_rest_left()
    {
        var plan = DropRules.Plan([File("a.png"), File("b.txt"), File("c.jpg", size: DroppedImage.MaxBytes)], "https://example.com", "text");
        Assert.Equal(DropOutcome.Images, plan.Outcome);
        Assert.Equal(["a.png", "c.jpg"], plan.Images.Select(f => f.Name));
        Assert.Null(plan.Text);
    }

    [Fact]
    public void Files_with_no_image_are_refused_even_with_text()
    {
        var plan = DropRules.Plan([File("b.txt")], null, "text");
        Assert.Equal(DropOutcome.NotPinnable, plan.Outcome);
        Assert.Empty(plan.Images);
    }

    [Fact]
    public void Images_all_over_40_MB_are_refused() =>
        Assert.Equal(DropOutcome.BadImage, DropRules.Plan([File("big.png", size: DroppedImage.MaxBytes + 1)], null, null).Outcome);

    [Fact]
    public void The_first_address_of_a_link_list_is_pinned_before_text()
    {
        var plan = DropRules.Plan([], "# a comment\r\n\r\n  https://example.com/a  \r\nhttps://example.com/b", "plain");
        Assert.Equal(DropOutcome.Text, plan.Outcome);
        Assert.Equal("https://example.com/a", plan.Text);
    }

    [Fact]
    public void Text_is_pinned_as_it_is_when_not_blank()
    {
        Assert.Equal("  two\nlines ", DropRules.Plan([], "# only a comment", "  two\nlines ").Text);
        Assert.Equal(DropOutcome.Nothing, DropRules.Plan([], null, " \r\n\t").Outcome);
        Assert.Equal(DropOutcome.Nothing, DropRules.Plan([], "", null).Outcome);
    }

    [Theory]
    [InlineData("https://a.example\nhttps://b.example", "https://a.example")]
    [InlineData("#x\n# y\n", null)]
    [InlineData("  \n\thttp://c.example\t", "http://c.example")]
    public void A_link_list_gives_its_first_address(string list, string? first) =>
        Assert.Equal(first, DropRules.FirstAddress(list));
}
