using System.Globalization;
using Tack.Core.Pinboard.Words;
using Tack.Core.Prints;

namespace Tack.Core.Tests.Pinboard.Words;

/// <summary>What a screen reader hears for a linked file: its name, type, size and age.</summary>
public class FileLabelsTests
{
    static readonly long Now = new DateTimeOffset(2026, 10, 22, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    static Print Linked(string name, FileFacts? facts, bool kept = false) => new()
    {
        Id = "1",
        Path = $@"C:\Users\me\{name}",
        Name = name,
        Kind = Kind.File,
        File = facts,
        PinnedAt = Now - 2 * 60_000,
        KeptAt = kept ? Now : null,
    };

    [Fact]
    public void A_file_says_its_name_type_size_and_age()
    {
        Assert.Equal(
            "Report.docx, Microsoft Word Document, 2 MB, 2 minutes ago",
            Labels.For(Linked("Report.docx", new FileFacts(false, 2 * 1024 * 1024, "Microsoft Word Document")), Now));
        Assert.Equal(
            "notes, 12 bytes, 2 minutes ago, kept",
            Labels.For(Linked("notes", new FileFacts(false, 12, " "), kept: true), Now));
    }

    [Fact]
    public void A_folder_says_it_is_one()
    {
        Assert.Equal("Projects, folder, 2 minutes ago", Labels.For(Linked("Projects", new FileFacts(true, 0, "File folder")), Now));
    }

    [Fact]
    public void A_file_with_no_facts_or_name_still_has_a_label()
    {
        Assert.Equal("a.zip, 2 minutes ago", Labels.For(Linked("a.zip", null), Now));
        Assert.Equal("File, 2 minutes ago", Labels.For(Linked("", null), Now));
    }

    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024 - 1, "10 KB")]
    [InlineData(340 * 1024, "340 KB")]
    [InlineData(2 * 1024 * 1024, "2 MB")]
    [InlineData(24_500_000, "23 MB")]
    [InlineData(3L * 1024 * 1024 * 1024 / 2, "1.5 GB")]
    [InlineData(5L * 1024 * 1024 * 1024 * 1024 * 1024, "5120 TB")]
    [InlineData(-4, "0 bytes")]
    public void A_size_is_in_explorers_units(long bytes, string words) =>
        Assert.Equal(words, FileSize.Words(bytes));

    [Fact]
    public void A_size_takes_the_cultures_decimal_mark() =>
        Assert.Equal("1,5 MB", FileSize.Words(3 * 1024 * 1024 / 2, CultureInfo.GetCultureInfo("de-DE")));
}
