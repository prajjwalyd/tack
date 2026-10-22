using Tack.Core.Phone;
using Tack.Core.Prints;
using Tack.Core.Settings;
using Tack.Core.Storage;
using static Tack.Core.Tests.Storage.StorageFixture;

namespace Tack.Core.Tests.Storage;

public class BoardFileParseTests
{
    const string V0_1_1 = """
        {
          "version": 3,
          "prints": [
            {
              "path": "C:\\Users\\me\\AppData\\Local\\Tack\\Captures\\Note 1.txt",
              "kind": "note",
              "capture": true,
              "pinnedAt": 1790000003000,
              "kept": false,
              "keptAt": null
            },
            {
              "path": "C:\\Users\\me\\Pictures\\Screenshots\\kept.png",
              "capture": false,
              "pinnedAt": 1790000001000,
              "kept": true,
              "keptAt": 1790000002000
            },
            {
              "path": "C:\\Users\\me\\AppData\\Local\\Tack\\Captures\\Screenshot 2026-10-08 023223.png",
              "capture": true,
              "pinnedAt": 1790000000000,
              "kept": false,
              "keptAt": null
            }
          ],
          "sound": false,
          "edgeReveal": true,
          "snipTipShown": true,
          "toggleShortcut": "Win+Alt+T",
          "pinShortcut": "Win+Alt+C",
          "phone": {
            "on": true,
            "devices": [
              {
                "key": "cGl4ZWw=",
                "name": "pixel",
                "approvedAt": 1790000004000
              }
            ]
          }
        }
        """;

    [Fact]
    public void A_board_json_as_v0_1_1_writes_it_loads_unchanged()
    {
        var (saved, loaded) = Parse(V0_1_1);

        Assert.Equal(Loaded.Ok.Instance, loaded);
        var device = new Device { Key = "cGl4ZWw=", Name = "pixel", ApprovedAt = 1_790_000_004_000 };
        var expected = new TackSettings
        {
            Sound = false,
            SnipTipShown = true,
            ToggleShortcut = "Win+Alt+T",
            Phone = new PhoneSettings { On = true, Devices = [device] },
        };
        Assert.Equal(expected, saved.Settings);
        const string Captures = @"C:\Users\me\AppData\Local\Tack\Captures";
        Assert.Equal(
            [
                // Kept prints come first, then the rest newest first.
                Saved(@"C:\Users\me\Pictures\Screenshots\kept.png", Origin.Folder, 1_790_000_001_000, 1_790_000_002_000),
                new SavedPrint($@"{Captures}\Note 1.txt", Kind.Note, Origin.Capture, 1_790_000_003_000, null),
                Saved($@"{Captures}\Screenshot 2026-10-08 023223.png", Origin.Capture, 1_790_000_000_000, null),
            ],
            saved.Prints);
    }

    [Fact]
    public void Missing_fields_take_their_defaults()
    {
        var (saved, loaded) = Parse("""
            { "prints": [
                { "path": "C:\\Pics\\a.png" },
                { "path": "C:\\Pics\\b.png", "kept": true, "pinnedAt": 5 },
                { "path": "C:\\Pics\\c.png", "kept": false, "keptAt": 9, "pinnedAt": 7 }
            ] }
            """);

        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.Equal(new TackSettings(), saved.Settings);
        Assert.Equal(
            [
                // Kept without a time: kept when pinned.
                Saved(@"C:\Pics\b.png", Origin.Folder, 5, 5),
                Saved(@"C:\Pics\a.png", Origin.Folder, Now, null),
                // Not kept, whatever keptAt says.
                Saved(@"C:\Pics\c.png", Origin.Folder, 7, null),
            ],
            saved.Prints);
    }

    [Fact]
    public void Known_versions_are_understood()
    {
        for (var v = 1; v <= 3; v++)
        {
            Assert.Equal(Loaded.Ok.Instance, Parse($$"""{ "version": {{v}}, "prints": [] }""").Loaded);
        }
    }

    [Fact]
    public void Notes_load_as_tacks_own_files_and_unknown_kinds_are_skipped()
    {
        var (saved, loaded) = Parse("""
            { "version": 3, "prints": [
                { "path": "C:\\Notes\\Note 1.txt", "kind": "note", "pinnedAt": 9 },
                { "path": "C:\\Pics\\b.png", "pinnedAt": 8 },
                { "path": "C:\\Else\\c.ink", "kind": "sketch", "pinnedAt": 7 }
            ], "togglePinShortcut": "ignored", "pinShortcut": "" }
            """);

        Assert.Equal(Loaded.Ok.Instance, loaded);
        Assert.Equal(2, saved.Prints.Count);
        Assert.Equal((Kind.Note, Origin.Capture), (saved.Prints[0].Kind, saved.Prints[0].Origin));
        Assert.Equal(Kind.Image, saved.Prints[1].Kind);
        Assert.Equal("", saved.Settings.PinShortcut);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ \"version\": 2, \"prints\": [ { \"path\": \"C:\\\\a")]
    public void Damaged_contents_are_an_empty_board_that_cannot_be_trusted(string json)
    {
        var (saved, loaded) = Parse(json);

        Assert.Empty(saved.Prints);
        Assert.Equal(new TackSettings(), saved.Settings);
        Assert.IsType<Loaded.Unreadable>(loaded);
    }

    [Theory]
    [InlineData("""{ "prints": [], "sound": null }""")]
    [InlineData("""{ "prints": [], "toggleShortcut": 3 }""")]
    [InlineData("""{ "prints": [], "phone": { "devices": [ { "key": "k", "name": "n" } ] } }""")]
    [InlineData("""{ "prints": [ { "kind": "note" } ] }""")]
    [InlineData("""{ "prints": [ { "path": "C:\\a.png", "capture": null } ] }""")]
    public void A_wrong_type_outside_the_lenient_choices_fails_the_file_as_it_did(string json) =>
        Assert.IsType<Loaded.Unreadable>(Parse(json).Loaded);

    [Fact]
    public void A_newer_schema_is_read_but_not_trusted()
    {
        var (saved, loaded) = Parse("""
            { "version": 99, "prints": [ { "path": "C:\\Cap\\a.png", "capture": true, "pinnedAt": 5 } ],
              "somethingNew": [1, 2, 3] }
            """);

        Assert.Equal([Saved(@"C:\Cap\a.png", Origin.Capture, 5, null)], saved.Prints);
        Assert.IsType<Loaded.Unreadable>(loaded);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "version": 3, "sound": false }""")]
    [InlineData("""{ "paths": ["C:\\a.png"] }""")]
    public void A_board_json_without_a_print_list_is_not_trusted(string json)
    {
        var (saved, loaded) = Parse(json);

        Assert.Empty(saved.Prints);
        Assert.IsType<Loaded.Unreadable>(loaded);
    }

    [Fact]
    public void An_empty_print_list_is_a_list() =>
        Assert.Equal(Loaded.Ok.Instance, Parse("""{ "version": 3, "prints": [] }""").Loaded);

    [Fact]
    public void Saved_prints_past_the_history_are_dropped()
    {
        var saved = new Saved();
        saved.Prints.Add(Saved("kept", Origin.Folder, Now - 9 * History.MaxAgeMs, Now - History.MaxAgeMs));
        saved.Prints.Add(Saved("old", Origin.Capture, Now - History.MaxAgeMs, null));
        for (var n = 0; n < History.MaxUnkept + 1; n++)
        {
            saved.Prints.Add(Saved(n.ToString(), Origin.Folder, Now - 10 - n, null));
        }

        var aged = saved.AgeOut(Now);

        Assert.Equal(["old", "50"], aged.Select(p => p.Path));
        Assert.Equal(History.MaxUnkept + 1, saved.Prints.Count);
        Assert.Equal("kept", saved.Prints[0].Path);
    }
}
