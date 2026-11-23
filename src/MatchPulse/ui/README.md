# MatchPulse UI – HTML/JS-Vorlage

Die Oberfläche von MatchPulse als statische Web-Seite. Sie ist für **WebView2** in der WinForms-App gedacht und läuft auch ohne App: `index.html` im Browser öffnen, dann antwortet ein Mock mit Demo-Daten (`?page=live` usw. öffnet direkt eine Seite).

Kein Framework, kein Build-Schritt, keine Netzwerkzugriffe.

## Dateien

| Datei | Inhalt | Ändern, wenn … |
|---|---|---|
| `index.html` | Gerüst: Seitenleiste, Inhaltsbereich, Skripte | sich das Grundlayout ändert |
| `css/tokens.css` | Farben (dunkel/hell), Schriften, Radien, Abstände | das Aussehen angepasst wird |
| `css/app.css` | Bausteine (Karten, Knöpfe, Listen, Formulare), nutzt nur Tokens | neue Bausteine nötig sind |
| `js/i18n.js` | Alle Texte, Deutsch und Englisch, gleiche Schlüssel | Texte oder Sprachen dazukommen |
| `js/mock-data.js` | Demo-Daten im Format der Nachricht `snapshot` | – (in der App entfernen) |
| `js/bridge.js` | Verbindung zum Host über `chrome.webview`, sonst Mock | sich das Nachrichtenformat ändert |
| `js/app.js` | Zustand, Seiten, Aktionen | Funktionen dazukommen |

Seiten: Übersicht, Ereignisse, Live-Monitor, Ziele, Plugins (OBS-Steuerung, Audio-Steuerung), Einstellungen, Diagnose, Protokoll.

In MatchPulse ist die Seite Plugins noch ausgeblendet (nur in der Browser-Vorschau sichtbar). Bilder kommen von `https://matchpulse.local/diagnostics/…` und `https://matchpulse.local/kills/…` (Ordner `%APPDATA%\MatchPulse`).

## Einbinden in WinForms (WebView2)

```csharp
// NuGet: Microsoft.Web.WebView2
await webView.EnsureCoreWebView2Async();
var core = webView.CoreWebView2;

// UI-Ordner und Bilder (Diagnose, Kill-Highlights) als virtuelle Hosts
core.SetVirtualHostNameToFolderMapping("app.matchpulse", uiFolder, CoreWebView2HostResourceAccessKind.DenyCors);
core.SetVirtualHostNameToFolderMapping("matchpulse.local", imagesFolder, CoreWebView2HostResourceAccessKind.DenyCors);

core.WebMessageReceived += (s, e) =>
{
    var msg = JsonDocument.Parse(e.WebMessageAsJson).RootElement;
    switch (msg.GetProperty("type").GetString())
    {
        case "ready":          Post("snapshot", BuildSnapshot()); break;
        case "saveSettings":   /* msg.payload → Settings */ break;
        case "saveSinks":      /* msg.payload → Liste der Ziele */ break;
        case "sendTestEvent":  /* msg.payload.sinkIds */ break;
        case "savePlugin":     /* msg.payload → Plugin */ break;
        case "testPlugin":     /* msg.payload.id */ break;
        case "setLiveMonitor": /* msg.payload.enabled */ break;
        // openLiveWindow, openDiagnosticsFolder, clearDiagnostics
    }
};

void Post(string type, object payload) =>
    core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, payload }, jsonCamelCase));

webView.Source = new Uri("https://app.matchpulse/index.html");
```

Für die App `js/mock-data.js` aus `index.html` entfernen (er wird in WebView2 ohnehin nicht benutzt). Die CSP in `index.html` erlaubt Bilder von `https://matchpulse.local`. Passe sie an, wenn du einen anderen Hostnamen nimmst.

Das Design folgt `prefers-color-scheme`, also dem Windows-Design, solange die Einstellung „Wie Windows“ gewählt ist.

## Nachrichten

Jede Nachricht ist `{ "type": "…", "payload": … }`. Zeiten sind ISO 8601 mit Offset.

### Host → UI

| type | payload | Wann |
|---|---|---|
| `snapshot` | ganzer Zustand, siehe unten | Antwort auf `ready` |
| `state` | `{ appState, match }` | Zustandswechsel (Spiel startet, Match beginnt/endet) |
| `gameEvent` | Game Event, genau wie an die Ziele | bei jedem Ereignis |
| `killHighlight` | `{ matchId, sequence, capturedAt, imageUrl }` | nach einem Kill, wenn der Live-Monitor an ist |
| `sinks` | Liste der Ziele | nach jedem Senden (Erfolg/Fehler) |
| `pluginStatus` | `{ id, status }` | Verbindungsänderung eines Plugins |
| `settings` | Einstellungen | wenn sie sich außerhalb der UI ändern |
| `diagnostics` | Liste der Fälle | neuer Fall, Ordner geleert |
| `hostProblems` | `["capture_failed", "config_invalid"]` | wenn sich Probleme ändern |
| `log` | Liste von Log-Zeilen | neue Zeilen |
| `navigate` | `{ page }` | z. B. Klick auf Windows-Benachrichtigung → `"sinks"` |

### UI → Host

| type | payload |
|---|---|
| `ready` | – |
| `saveSettings` | Einstellungen |
| `saveSinks` | Liste der Ziele (gilt sofort) |
| `sendTestEvent` | `{ sinkIds: [...] }` |
| `savePlugin` | ein Plugin (gilt sofort) |
| `testPlugin` | `{ id }` |
| `setLiveMonitor` | `{ enabled }` |
| `openLiveWindow`, `openDiagnosticsFolder`, `clearDiagnostics` | – |

### snapshot

```jsonc
{
  "appState": "off | game | match",
  "match": { "matchId": "91fdb6ce…", "startedAt": "2026-11-23T20:15:02+01:00" },   // oder null
  "hostProblems": [],
  "events": [ { "type": "kill", "matchId": "…", "sequence": 2, "occurredAt": "…", "data": {} } ],
  "sinks": [ { "id": "s1", "kind": "webhook | script", "target": "…",
               "lastSuccessAt": "…|null", "lastFailureAt": "…|null",
               "lastError": { "code": "timeout | script_timeout | …", "message": "…" } } ],
  "plugins": [
    { "id": "obs", "enabled": true, "status": "connected | disconnected | error",
      "config": { "host": "localhost", "port": 4455, "password": "…" },
      "rules": [ { "id": "r1", "event": "kill", "action": "save_replay", "param": "" } ] },
    { "id": "audio", "enabled": false, "status": "ready",
      "config": { "apps": [ { "name": "Spotify.exe", "volume": 20 } ], "restoreOnExit": true },
      "rules": [ { "id": "r4", "event": "match_started", "action": "duck", "param": "" } ] }
  ],
  "settings": { "gameLanguage": "de", "uiLanguage": "de", "theme": "system | light | dark",
                "killMarkerColor": "#FF3030", "checksPerSecond": 10, "matchEndWithoutHudSeconds": 120,
                "diagnosticMode": true, "notifyOnProblems": true, "liveMonitor": false },
  "highlights": [ { "matchId": "…", "sequence": 2, "capturedAt": "…", "imageUrl": "https://matchpulse.local/kills/….png" } ],
  "diagnostics": [ { "id": "d1", "capturedAt": "…", "kind": "skull_without_marker | marker_without_skull", "imageUrl": "…|null" } ],
  "log": [ { "at": "…", "level": "INFO | EVENT | WARN | ERROR", "message": "…" } ]
}
```

Plugin-Aktionen: OBS `start_recording`, `stop_recording`, `save_replay`, `switch_scene` (`param` = Name der Szene). Audio `duck` (senkt die Programme auf ihre `volume` in Prozent), `restore`.

`imageUrl: null` zeigt einen Platzhalter. Das ist nützlich, solange die Bildaufnahme noch fehlt.

## Erweitern

- **Text:** Schlüssel in `de` und `en` von `js/i18n.js` ergänzen, im Code `t('schlüssel')`.
- **Seite:** Funktion `pageXyz()` in `app.js`, in `PAGES` und `NAV` eintragen, Icon-Pfad in `ICONS`, Text `nav_xyz`.
- **Plugin:** Eintrag im `snapshot`, Texte `pl_<id>`, `pl_<id>_d`, Icon `ICONS.<id>`, Aktionsliste in `pagePlugins()` und eigener Konfigurationsblock.
- **Aktion:** Element bekommt `data-act="name"`, Funktion in `ACT`. Feldänderungen über `data-change` (`CHG`), Tippen ohne Neuaufbau über `data-input` (`INP`).
