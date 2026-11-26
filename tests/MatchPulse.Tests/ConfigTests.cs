namespace MatchPulse.Tests;

public class ConfigTests
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"matchpulse-{Guid.NewGuid():N}", "config.json");

    [Fact]
    public void Saved_settings_are_loaded_back_after_a_restart()
    {
        var saved = new Config([new Uri("http://localhost:8080/bf6")], Scripts: [@"C:\obs\mark.cmd"], GameLanguage: "en", UiLanguage: "de", MatchNames: ["TEAM DEATHMATCH"]);

        saved.Save(_path);
        var loaded = Config.Load(_path);

        Assert.Equal(["http://localhost:8080/bf6"], loaded.Webhooks.Select(u => u.ToString()));
        Assert.Equal([@"C:\obs\mark.cmd"], loaded.Scripts);
        Assert.Equal("en", loaded.GameLanguage);
        Assert.Equal("de", loaded.UiLanguage);
        Assert.Equal(["TEAM DEATHMATCH"], loaded.MatchNames);
    }

    [Fact]
    public void Missing_config_file_gives_defaults()
    {
        var config = Config.LoadOrDefault(_path);

        Assert.Empty(config.Webhooks);
        Assert.Empty(config.Scripts);
        Assert.Equal("de", config.GameLanguage);
        Assert.Equal("en", config.UiLanguage);
    }

    [Theory]
    [InlineData("""{ "webhooks": [null] }""")]
    [InlineData("""{ "webhooks": [], "scripts": [null] }""")]
    [InlineData("""{ "webhooks": [], "scripts": [""] }""")]
    [InlineData("""{ "webhooks": [], "scripts": ["  "] }""")]
    public void Null_or_empty_webhook_or_script_is_rejected(string json)
    {
        File.WriteAllText(Directory.CreateDirectory(Path.GetDirectoryName(_path)!).FullName + "/config.json", json);

        Assert.Throws<InvalidDataException>(() => Config.Load(_path));
    }

    [Fact]
    public void Hud_lost_timeout_defaults_to_120_s_and_is_loaded_from_the_file()
    {
        Assert.Equal(120, Config.LoadOrDefault(_path).HudLostAfterSeconds);
        File.WriteAllText(Directory.CreateDirectory(Path.GetDirectoryName(_path)!).FullName + "/config.json",
            """{ "webhooks": [], "hudLostAfterSeconds": 300 }""");

        Assert.Equal(300, Config.Load(_path).HudLostAfterSeconds);
    }

    [Fact]
    public void Unknown_game_language_is_rejected()
    {
        File.WriteAllText(Directory.CreateDirectory(Path.GetDirectoryName(_path)!).FullName + "/config.json",
            """{ "webhooks": [], "gameLanguage": "fr" }""");

        Assert.Throws<InvalidDataException>(() => Config.Load(_path));
    }

    [Fact]
    public void Kill_Marker_colour_defaults_to_red_and_is_loaded_from_the_file()
    {
        Assert.Equal("#FF0000", Config.LoadOrDefault(_path).KillMarkerColor);
        File.WriteAllText(Directory.CreateDirectory(Path.GetDirectoryName(_path)!).FullName + "/config.json",
            """{ "webhooks": [], "killMarkerColor": "#00ff00" }""");

        Assert.Equal("#00ff00", Config.Load(_path).KillMarkerColor);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("FF0000")]
    [InlineData("#FF00")]
    [InlineData("#GG0000")]
    public void Kill_Marker_colour_that_is_not_a_hex_RGB_value_is_rejected(string color)
    {
        File.WriteAllText(Directory.CreateDirectory(Path.GetDirectoryName(_path)!).FullName + "/config.json",
            $$"""{ "webhooks": [], "killMarkerColor": "{{color}}" }""");

        Assert.Throws<InvalidDataException>(() => Config.Load(_path));
    }
}
