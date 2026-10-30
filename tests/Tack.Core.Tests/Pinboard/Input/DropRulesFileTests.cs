using Tack.Core.Captures;
using Tack.Core.Pinboard.Input;

namespace Tack.Core.Tests.Pinboard.Input;

/// <summary>Files and folders dropped on the board: linked where they are, pictures read as before, at most 20.</summary>
public class DropRulesFileTests
{
    static DroppedFile OnDisk(string name, long size = 1000, bool folder = false) =>
        new(name, null, size, $@"C:\Users\me\Documents\{name}", folder);

    [Fact]
    public void A_mixed_drop_pins_pictures_as_pictures_and_the_rest_where_they_are()
    {
        var plan = DropRules.Plan([OnDisk("shot.png"), OnDisk("Report.docx"), OnDisk("Projects", 0, folder: true), OnDisk("photo.jpg")], null, "text");

        Assert.Equal(DropOutcome.Files, plan.Outcome);
        Assert.Equal(["shot.png", "photo.jpg"], plan.Images.Select(f => f.Name));
        Assert.Equal(["Report.docx", "Projects"], plan.Links.Select(f => f.Name));
        Assert.Equal(0, plan.Over);
        Assert.Null(plan.Text);
    }

    [Fact]
    public void Only_files_is_a_drop_of_links()
    {
        var plan = DropRules.Plan([OnDisk("a.zip"), OnDisk("b.pdf")], null, null);

        Assert.Equal(DropOutcome.Files, plan.Outcome);
        Assert.Empty(plan.Images);
        Assert.Equal(2, plan.Links.Count);
    }

    [Fact]
    public void A_folder_named_like_a_picture_is_still_a_folder() =>
        Assert.Equal(["holiday.png"], DropRules.Plan([OnDisk("holiday.png", 0, folder: true)], null, null).Links.Select(f => f.Name));

    [Fact]
    public void A_picture_too_large_to_read_is_linked_where_it_is()
    {
        var plan = DropRules.Plan([OnDisk("huge.png", DroppedImage.MaxBytes + 1)], null, null);

        Assert.Equal(DropOutcome.Files, plan.Outcome);
        Assert.Empty(plan.Images);
        Assert.Equal(["huge.png"], plan.Links.Select(f => f.Name));
    }

    [Fact]
    public void At_most_20_items_are_pinned_and_the_rest_counted()
    {
        var files = Enumerable.Range(1, 26).Select(n => OnDisk(n % 2 == 0 ? $"{n}.png" : $"{n}.txt")).ToList();

        var plan = DropRules.Plan(files, null, null);

        Assert.Equal(DropRules.MaxItems, plan.Images.Count + plan.Links.Count);
        Assert.Equal(files.Take(20).Select(f => f.Name).Order(), plan.Images.Concat(plan.Links).Select(f => f.Name).Order());
        Assert.Equal(6, plan.Over);
    }

    [Fact]
    public void Exactly_20_items_leave_none_over() =>
        Assert.Equal(0, DropRules.Plan([.. Enumerable.Range(1, 20).Select(n => OnDisk($"{n}.txt"))], null, null).Over);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\server\share\a.docx")]
    [InlineData(@"\\?\C:\a.docx")]
    [InlineData(@"relative\a.docx")]
    public void Only_a_file_on_a_drive_letter_can_be_linked(string? path)
    {
        var file = new DroppedFile("a.docx", null, 10, path);

        Assert.False(DropRules.IsLinkable(file));
        Assert.Equal(DropOutcome.NotPinnable, DropRules.Plan([file], null, null).Outcome);
    }

    [Fact]
    public void Files_with_no_place_say_so_even_past_the_cap()
    {
        var plan = DropRules.Plan([.. Enumerable.Range(1, 22).Select(n => new DroppedFile($"{n}.txt", null, 1))], null, null);

        Assert.Equal(DropOutcome.NotPinnable, plan.Outcome);
        Assert.Equal(2, plan.Over);
    }

    [Fact]
    public void Pictures_with_no_place_still_pin_from_their_bytes()
    {
        var plan = DropRules.Plan([new DroppedFile("from-zip.png", "image/png", 10), new DroppedFile("from-zip.docx", null, 10)], null, null);

        Assert.Equal(DropOutcome.Files, plan.Outcome);
        Assert.Equal(["from-zip.png"], plan.Images.Select(f => f.Name));
        Assert.Empty(plan.Links);
    }
}
