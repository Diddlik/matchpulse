namespace MatchPulse;

/// <summary>Tray texts in German and English. The main window has its own texts in <c>ui/js/i18n.js</c>.</summary>
static class Ui
{
    static readonly Dictionary<string, (string De, string En)> Texts = new()
    {
        ["Open"] = ("MatchPulse öffnen", "Open MatchPulse"),
        ["Exit"] = ("Beenden", "Exit"),
        ["ConfigInvalid"] = ("Die Konfigurationsdatei ist ungültig; es gelten die Standardeinstellungen. Speichern in den Einstellungen überschreibt sie.",
            "The config file is invalid; defaults apply. Saving in Settings overwrites it."),
        ["SinkFailed"] = ("Ein Ziel hat ein Ereignis nicht angenommen. Klicken für Details.", "A target did not accept an event. Click for details."),
        ["CaptureFailed"] = ("Die Bildschirmaufnahme ist fehlgeschlagen und wird wiederholt.", "Screen capture failed and is being retried."),
        [nameof(TrayState.GameNotRunning)] = ("Spiel läuft nicht", "Game not running"),
        [nameof(TrayState.GameRunning)] = ("Spiel läuft", "Game running"),
        [nameof(TrayState.MatchRunning)] = ("Match läuft", "Match running"),
    };

    public static string T(string language, string key) => language == "de" ? Texts[key].De : Texts[key].En;
}
