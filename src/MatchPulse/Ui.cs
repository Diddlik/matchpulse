namespace MatchPulse;

/// <summary>UI texts in German and English.</summary>
static class Ui
{
    static readonly Dictionary<string, (string De, string En)> Texts = new()
    {
        ["Settings"] = ("Einstellungen", "Settings"),
        ["Log"] = ("Log", "Log"),
        ["Exit"] = ("Beenden", "Exit"),
        ["GameLanguage"] = ("Spielsprache", "Game language"),
        ["UiLanguage"] = ("Oberflächensprache", "UI language"),
        ["Webhooks"] = ("Webhook-URLs (eine pro Zeile)", "Webhook URLs (one per line)"),
        ["Scripts"] = ("Skripte (eines pro Zeile)", "Scripts (one per line)"),
        ["Save"] = ("Speichern", "Save"),
        ["Saved"] = ("Gespeichert und übernommen.", "Saved and applied."),
        ["Invalid"] = ("Ungültige Einstellungen", "Invalid settings"),
        ["Refresh"] = ("Aktualisieren", "Refresh"),
        ["ConfigInvalid"] = ("Die Konfigurationsdatei ist ungültig; es gelten die Standardeinstellungen. Speichern in den Einstellungen überschreibt sie.",
            "The config file is invalid; defaults apply. Saving in Settings overwrites it."),
        [nameof(TrayState.GameNotRunning)] = ("Spiel läuft nicht", "Game not running"),
        [nameof(TrayState.GameRunning)] = ("Spiel läuft", "Game running"),
        [nameof(TrayState.MatchRunning)] = ("Match läuft", "Match running"),
    };

    public static string T(string language, string key) => language == "de" ? Texts[key].De : Texts[key].En;
}
