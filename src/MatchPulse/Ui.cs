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
        ["NoPicture"] = ("MatchPulse bekommt kein Bild vom Spiel. Stelle BF6 auf randloses Fenster.", "MatchPulse gets no picture of the game. Set BF6 to borderless."),
        ["MqttFailed"] = ("MQTT: Ereignis nicht gesendet. Klicken für Details.", "MQTT: an event was not sent. Click for details."),
        ["WledFailed"] = ("WLED hat eine Regel nicht ausgeführt. Klicken für Details.", "WLED did not run a rule. Click for details."),
        ["SoundFailed"] = ("Ein Sound konnte nicht abgespielt werden. Klicken für Details.", "A sound could not be played. Click for details."),
        ["ObsFailed"] = ("OBS hat eine Regel nicht ausgeführt. Klicken für Details.", "OBS did not run a rule. Click for details."),
        ["UpdateAvailable"] = ("Eine neue Version von MatchPulse ist verfügbar. Klicken zum Installieren.", "A new version of MatchPulse is available. Click to install."),
        ["Updated"] = ("MatchPulse wurde auf Version {0} aktualisiert.", "MatchPulse was updated to version {0}."),
        ["Active"] = ("Aktiv", "Active"),
        ["Activated"] = ("MatchPulse aktiviert", "MatchPulse activated"),
        ["Deactivated"] = ("MatchPulse deaktiviert", "MatchPulse deactivated"),
        ["Paused"] = ("pausiert", "paused"),
        [nameof(TrayState.GameNotRunning)] = ("Spiel läuft nicht", "Game not running"),
        [nameof(TrayState.GameRunning)] = ("Spiel läuft", "Game running"),
        [nameof(TrayState.MatchRunning)] = ("Match läuft", "Match running"),
    };

    public static string T(string language, string key) => language == "de" ? Texts[key].De : Texts[key].En;
}
