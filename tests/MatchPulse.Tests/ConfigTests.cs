namespace MatchPulse.Tests;

public class ConfigTests
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"matchpulse-{Guid.NewGuid():N}", "config.json");

    [Fact]
    public void Saved_settings_are_loaded_back_after_a_restart()
    {
        var saved = new Config([new Uri("http://localhost:8080/bf6")], Scripts: [@"C:\obs\mark.cmd"], GameLanguage: "en", UiLanguage: "de");

        saved.Save(_path);
        var loaded = Config.Load(_path);

        Assert.Equal(["http://localhost:8080/bf6"], loaded.Webhooks.Select(u => u.ToString()));
        Assert.Equal([@"C:\obs\mark.cmd"], loaded.Scripts);
        Assert.Equal("en", loaded.GameLanguage);
        Assert.Equal("de", loaded.UiLanguage);
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

    [Fact]
    public void Unknown_game_language_is_rejected()
    {
        File.WriteAllText(Directory.CreateDirectory(Path.GetDirectoryName(_path)!).FullName + "/config.json",
            """{ "webhooks": [], "gameLanguage": "fr" }""");

        Assert.Throws<InvalidDataException>(() => Config.Load(_path));
    }
}
