using System.Text;
using Tack.Core.Notes;

namespace Tack.Core.Tests.Notes;

public class NoteTextTests
{
    [Fact]
    public void Blank_text_is_not_a_note()
    {
        Assert.Null(NoteText.Body(""));
        Assert.Null(NoteText.Body("  \r\n\t \n "));
        Assert.Null(NoteText.Body("\0"));
        Assert.Null(NoteText.Body("\u0001"));
        Assert.Equal("ab\tc", NoteText.Body("a\u0001b\tc")!.Text);
    }

    [Fact]
    public void Text_is_normalised_but_keeps_its_shape()
    {
        var note = NoteText.Body("\r\n  \r\n    fn main() {\r\n        go();\r\n    }\r\n\r\n  ")!;

        Assert.Equal("    fn main() {\n        go();\n    }", note.Text);
        Assert.False(note.Truncated);
        Assert.Null(note.Link);
        Assert.Equal("old\nmac", NoteText.Body("old\rmac")!.Text);
    }

    [Fact]
    public void Long_text_is_cut_at_a_character_boundary()
    {
        // 3-byte characters, so the limit falls inside one.
        var note = NoteText.Body(string.Concat(Enumerable.Repeat("€", NoteText.MaxNoteBytes / 3 + 10)))!;
        var bytes = Encoding.UTF8.GetByteCount(note.Text);

        Assert.True(note.Truncated);
        Assert.InRange(bytes, NoteText.MaxNoteBytes - 2, NoteText.MaxNoteBytes);
        Assert.All(note.Text, c => Assert.Equal('€', c));
        // Exactly at the limit is not truncated.
        var exact = new string('a', NoteText.MaxNoteBytes);
        Assert.False(NoteText.Body(exact)!.Truncated);
        Assert.True(NoteText.Body(exact + "b")!.Truncated);
    }

    [Fact]
    public void A_lone_web_address_is_a_link()
    {
        var note = NoteText.Body("  https://www.GitHub.com/tauri-apps/tauri/releases?x=1#top \n")!;

        Assert.Equal("https://www.GitHub.com/tauri-apps/tauri/releases?x=1#top", note.Link);
        Assert.Equal("github.com", note.Domain);
        Assert.Equal("HTTP://example.org", NoteText.LinkOf("HTTP://example.org"));
        Assert.Equal("https://www.example.org/a", NoteText.LinkOf("www.example.org/a"));
        Assert.Equal("docs.rs", NoteText.DomainOf("https://user:pw@docs.rs:8080/x"));
        Assert.Equal("[::1]", NoteText.DomainOf("http://[::1]:5178/"));
    }

    [Theory]
    [InlineData("see https://example.org")]
    [InlineData("https://example.org and more")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("mailto:someone@example.org")]
    [InlineData("javascript:alert(1)")]
    [InlineData(@"C:\Users\me\notes.txt")]
    [InlineData("https://")]
    [InlineData("https:///path")]
    [InlineData("https://exa mple.org")]
    [InlineData("https://example.org/\"quoted\"")]
    [InlineData("example.org")]
    public void Anything_else_is_plain_text(string text)
    {
        var note = NoteText.Body(text)!;

        Assert.Null(note.Link);
        Assert.Null(note.Domain);
    }

    [Fact]
    public void A_link_longer_than_the_limit_is_plain_text() =>
        Assert.Null(NoteText.LinkOf("https://example.org/" + new string('a', NoteText.MaxLinkBytes)));
}
