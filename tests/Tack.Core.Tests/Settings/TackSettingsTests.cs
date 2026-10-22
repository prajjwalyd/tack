using Tack.Core.Appearance;
using Tack.Core.Reveal;
using Tack.Core.Settings;
using Tack.Core.Storage;
using static Tack.Core.Tests.Storage.StorageFixture;

namespace Tack.Core.Tests.Settings;

/// <summary>The settings as board.json holds them, read and written the way the app does.</summary>
public class TackSettingsTests
{
    static TackSettings Read(string fields) => Parse($$"""{ "prints": [], {{fields}} }""").Saved.Settings;

    static string Written(TackSettings settings)
    {
        using var scratch = new Scratch("settings");
        var file = Path.Combine(scratch.Path, "board.json");
        BoardFile.Write(file, Snapshot.Of([], [], settings));
        return File.ReadAllText(file);
    }

    [Fact]
    public void The_new_settings_default_to_the_way_it_always_was()
    {
        var settings = new TackSettings();
        Assert.Equal(RevealSpeed.Normal, settings.Reveal);
        Assert.Equal(Felt.Default, settings.Appearance.Felt);
        Assert.Equal("aluminium", settings.Appearance.Frame.Id);
        Assert.Equal("white", settings.Appearance.Paper.Id);
        Assert.True(settings.Updates.Check);
    }

    [Fact]
    public void They_are_saved_as_camel_case_json()
    {
        var json = Written(new TackSettings());
        Assert.Contains("\"reveal\": \"normal\"", json);
        Assert.Contains("\"appearance\": {\n    \"felt\": \"green\",\n    \"frame\": \"aluminium\",\n    \"paper\": \"white\"\n  }", json);
        Assert.Contains("\"updates\": {\n    \"check\": true\n  }", json);
    }

    [Fact]
    public void They_survive_a_round_trip()
    {
        var settings = new TackSettings
        {
            Reveal = RevealSpeed.Instant,
            Appearance = new AppearanceSettings { Felt = Felt.Parse("#A1B2C3"), Frame = Frame.Parse("brass"), Paper = NotePaper.Parse("mint") },
            Updates = new UpdateSettings { Check = false },
        };
        var json = Written(settings);
        Assert.Contains("\"felt\": \"#a1b2c3\",\n    \"frame\": \"brass\",\n    \"paper\": \"mint\"", json);
        Assert.Equal(settings, Parse(json).Saved.Settings);
    }

    [Fact]
    public void A_file_from_before_them_still_loads_with_the_defaults()
    {
        var settings = Read("""  "sound": false, "edgeReveal": false, "toggleShortcut": "Win+Alt+Q" """);
        Assert.False(settings.Sound || settings.EdgeReveal);
        Assert.Equal("Win+Alt+Q", settings.ToggleShortcut);
        Assert.Equal(RevealSpeed.Normal, settings.Reveal);
        Assert.Equal(new AppearanceSettings(), settings.Appearance);
        Assert.True(settings.Updates.Check);
        // Or with only part of a group.
        Assert.Equal(new TackSettings(), Read("""  "appearance": {}, "updates": {} """));
    }

    [Fact]
    public void Bad_values_fall_back_and_never_stop_the_file_loading()
    {
        var settings = Read("""  "sound": false, "reveal": "turbo", "appearance": { "felt": "#12", "frame": "gold", "paper": 3 }, "updates": { "check": false } """);
        Assert.Equal(RevealSpeed.Normal, settings.Reveal);
        Assert.Equal(new AppearanceSettings(), settings.Appearance);
        Assert.False(settings.Sound || settings.Updates.Check, "the good values stay");
        var wrongTypes = Read("""  "reveal": 3, "appearance": { "felt": null, "frame": [], "paper": {} } """);
        Assert.Equal((RevealSpeed.Normal, new AppearanceSettings()), (wrongTypes.Reveal, wrongTypes.Appearance));
    }

    [Fact]
    public void Each_appearance_setting_falls_back_on_its_own()
    {
        var appearance = Read("""  "appearance": { "felt": "navy", "frame": "gold", "paper": "blush" } """).Appearance;
        Assert.Equal("navy", appearance.Felt.ToString());
        Assert.Equal(Frame.Default, appearance.Frame);
        Assert.Equal("blush", appearance.Paper.Id);
    }

    [Fact]
    public void A_file_from_before_the_frame_and_paper_keeps_its_felt()
    {
        var appearance = Read("""  "appearance": { "felt": "plum" } """).Appearance;
        Assert.Equal("plum", appearance.Felt.ToString());
        Assert.Equal((Frame.Default, NotePaper.Default), (appearance.Frame, appearance.Paper));
    }

    [Fact]
    public void Shortcuts_default_to_win_alt_and_survive_a_bad_value()
    {
        var settings = new TackSettings();
        Assert.Equal("Win+Alt+S", settings.ToggleChord().ToString());
        Assert.Equal("Win+Alt+C", settings.PinChord().ToString());
        var odd = settings with { ToggleShortcut = "Shift+T", PinShortcut = "" };
        Assert.Equal("Win+Alt+S", odd.ToggleChord().ToString());
        Assert.Null(odd.PinChord());
    }
}
