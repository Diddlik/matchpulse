using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace MatchPulse.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+M", 0x3u, Keys.M)]
    [InlineData("Strg+Alt+M", 0x3u, Keys.M)]
    [InlineData("Shift+F9", 0x4u, Keys.F9)]
    [InlineData("Win+Ctrl+7", 0xAu, Keys.D7)]
    [InlineData("ctrl + numpad5", 0x2u, Keys.NumPad5)]
    public void Valid_hotkeys_are_parsed(string text, uint modifiers, Keys key)
    {
        Assert.Equal((modifiers, key), Hotkey.Parse(text));
    }

    [Theory]
    [InlineData("M")]            // no modifier
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+M+N")]     // two keys
    [InlineData("Ctrl+Escape")]  // not an allowed key
    [InlineData("Ctrl+Banana")]
    public void Invalid_hotkeys_are_rejected(string text)
    {
        Assert.Null(Hotkey.Parse(text));
    }

    [Fact]
    public void Config_accepts_a_valid_or_empty_hotkey_and_rejects_others()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mp-hotkey-{Guid.NewGuid():N}.json");
        try
        {
            new Config([], ToggleHotkey: "Ctrl+Shift+P").Save(path);
            new Config([], ToggleHotkey: "").Save(path);
            Assert.Throws<InvalidDataException>(() => new Config([], ToggleHotkey: "P").Save(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Window_switch_and_hotkey_map_to_the_config()
    {
        var config = new Config([]);
        var applied = new List<Config>();
        var hub = new UiHub(() => config, c => { applied.Add(c); config = c; }, c => [], Path.GetTempPath());

        hub.Receive("""{"type":"setActive","payload":{"active":false}}""");
        Assert.False(config.Active);

        var saved = UiHub.SettingsFrom(config, JsonNode.Parse("""{"active":true,"toggleHotkey":" Ctrl+Alt+P "}""")!.AsObject());
        Assert.Equal((true, "Ctrl+Alt+P"), (saved.Active, saved.ToggleHotkey));
        Assert.Equal("Ctrl+Alt+M", new Config([]).ToggleHotkey);
    }
}
