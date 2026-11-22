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
        ["KillMarkerColor"] = ("Farbe der Kill-Markierung (wie im Spiel eingestellt, gilt ab dem nächsten Spielstart)",
            "Kill Marker colour (as set in the game, applies from the next game start)"),
        ["KillMarkerNotDistinct"] = ("Diese Farbe ist vom weißen Treffermarker nicht zu unterscheiden: jedes Kill-Abzeichen zählt als Kill, auch vom Squad erledigte Knockdowns.",
            "This colour cannot be told from the white hit marker: every Kill Badge counts as a kill, Squad-finished knockdowns included."),
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
