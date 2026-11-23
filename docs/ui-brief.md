# MatchPulse: Beschreibung für das UI-Design

Dieses Dokument beschreibt, was MatchPulse macht, wer es benutzt und welche Informationen und Einstellungen die Oberfläche zeigen muss. Es ist die Grundlage für ein neues UI-Design.

## Was MatchPulse ist

MatchPulse ist ein kleines Windows-Programm für Spieler von Battlefield 6. Es läuft im Hintergrund, während der Spieler spielt. Es schaut sich das Spielbild an und erkennt dabei bestimmte Ereignisse:

- ein Match beginnt,
- der Spieler macht einen Kill,
- ein Match endet, mit Sieg oder Niederlage.

Jedes erkannte Ereignis schickt MatchPulse sofort an andere Programme weiter. Das können ein Webhook (eine Web-Adresse) oder ein Skript auf dem PC sein. Damit kann der Spieler zum Beispiel OBS automatisch aufnehmen lassen, Clips von Kills schneiden, eine Statistik führen oder eine Lampe blinken lassen.

MatchPulse nimmt selbst nichts auf. Es ist nur die Quelle der Ereignisse. Was mit den Ereignissen passiert, entscheiden die angeschlossenen Programme.

MatchPulse greift nicht in das Spiel ein. Es liest nur das Bild vom Bildschirm, so wie eine Aufnahmesoftware. Es ist deshalb für den Anti-Cheat unbedenklich.

## Wer es benutzt

- Ein einzelner Spieler auf seinem eigenen Windows-PC.
- Er spielt meistens im Vollbild. Während des Spielens sieht er MatchPulse nicht.
- Er öffnet MatchPulse nur, um es einzurichten, um zu prüfen, ob es funktioniert, oder um nach einer Runde nachzusehen, was erkannt wurde.
- Er ist technisch interessiert und richtet Webhooks oder Skripte selbst ein, ist aber kein Programmierer.
- Die Oberfläche gibt es auf Deutsch und auf Englisch.

## Begriffe

| Begriff | Bedeutung |
|---|---|
| Game Event | Ein erkanntes Ereignis: `match_started`, `kill` oder `match_ended`. |
| Match | Eine Runde im Battle-Royale-Modus REDSEC, vom Absprung bis zum Sieg- oder Niederlagebildschirm. Im normalen Multiplayer werden Runden noch nicht erkannt. |
| Kill | Der Spieler hat einen Gegner selbst getötet. Kills, die das Squad erledigt, zählen nicht. |
| Kill-Markierung | Das Kreuz um das Fadenkreuz, das bei einem Treffer erscheint. Die Farbe stellt der Spieler im Spiel ein. |
| Kill-Abzeichen | Der Totenkopf im Punkte-Popup unter dem Fadenkreuz. |
| Störsender (Jammer) | Ein Effekt im Spiel, der für 30 bis 60 Sekunden Teile der Anzeige verdeckt. Kills werden trotzdem erkannt. |
| Ziel (Sink) | Wohin die Ereignisse geschickt werden: Webhook oder Skript. |

## Wie die Erkennung funktioniert (kurz)

- MatchPulse wartet, bis Battlefield 6 (`bf6.exe`) gestartet wird.
- Dann schaut es 5 bis 10 Mal pro Sekunde auf bestimmte Stellen des Bildschirms.
- Ein Kill wird gemeldet, wenn erst die Kill-Markierung und kurz danach (bis 1,5 s) ein Totenkopf im Punkte-Popup erscheint.
- Ein Match beginnt, wenn die REDSEC-Anzeige oben rechts erscheint. Es endet beim Sieg- oder Niederlagebildschirm, wenn das Spiel beendet wird, oder wenn die Anzeige 2 Minuten lang fehlt.
- Wird das Spiel beendet, hört MatchPulse auf zu schauen und braucht fast keine Rechenleistung mehr.

## Die Ereignisse

Jedes Ereignis ist ein kleines JSON-Dokument. Beispiele:

```json
{ "type": "match_started", "matchId": "91fdb6ce…", "sequence": 1, "occurredAt": "2026-11-23T20:15:02+01:00", "data": {} }
{ "type": "kill", "matchId": "91fdb6ce…", "sequence": 2, "occurredAt": "2026-11-23T20:18:40+01:00", "data": {} }
{ "type": "match_ended", "matchId": "91fdb6ce…", "sequence": 3, "occurredAt": "2026-11-23T20:34:11+01:00", "data": { "result": "victory", "reason": "end_screen" } }
```

- `matchId` gehört zu einer Runde. Bei Kills im normalen Multiplayer ist sie leer (`null`).
- `sequence` zählt die Ereignisse einer Runde ab 1.
- `result` ist `victory`, `defeat` oder `unknown`.
- `reason` sagt, warum das Match endete: `end_screen` (Endbildschirm), `hud_lost` (Anzeige zu lange weg), `game_exited` (Spiel beendet), `stopped` (MatchPulse beendet).

## Zustände des Programms

Das Programm ist immer in genau einem dieser Zustände:

1. **Spiel läuft nicht.** MatchPulse wartet.
2. **Spiel läuft.** MatchPulse schaut zu, aber es läuft kein Match (Menü, Lobby oder normaler Multiplayer).
3. **Match läuft.** Eine REDSEC-Runde ist erkannt.

Zusätzlich kann es Probleme geben, die der Spieler sehen sollte:

- Ein Ziel ist nicht erreichbar (Webhook antwortet nicht, Skript schlägt fehl oder hängt).
- Die Bildschirmaufnahme schlägt fehl und wird wiederholt.
- Die Konfigurationsdatei ist ungültig; dann gelten die Standardwerte.
- Die eingestellte Farbe der Kill-Markierung ist weiß, grau oder sehr dunkel; dann zählt jeder Totenkopf als Kill, auch Kills des Squads.

## Die heutige Oberfläche

Heute gibt es nur ein Symbol im Infobereich der Taskleiste und ein Einstellungsfenster.

**Symbol in der Taskleiste**

- Farbe zeigt den Zustand: grau = Spiel läuft nicht, orange = Spiel läuft, grün = Match läuft.
- Tooltip mit dem Zustand als Text.
- Menü: Einstellungen, Beenden. Doppelklick öffnet die Einstellungen.

**Einstellungsfenster**

- Spielsprache (Deutsch oder Englisch, wichtig für Texte im Spiel).
- Oberflächensprache (Deutsch oder Englisch).
- Farbe der Kill-Markierung, mit Farbauswahl und Hinweis, wenn die Farbe ungeeignet ist.
- Webhook-URLs, eine pro Zeile.
- Skripte, eines pro Zeile. Jedes Skript bekommt das Ereignis als JSON.
- Knopf „Speichern“. Webhooks und Skripte gelten sofort, Sprache und Farbe ab dem nächsten Spielstart.
- Reiter „Log“ mit dem Protokoll und einem Knopf „Aktualisieren“.

## Alle Einstellungen

| Einstellung | Werte | Standard | Bedeutung |
|---|---|---|---|
| Webhooks | Liste von http(s)-Adressen | leer | Jedes Ereignis wird an jede Adresse geschickt. |
| Skripte | Liste von Befehlszeilen | leer | Jedes Ereignis startet jedes Skript einmal. Abbruch nach 10 s. |
| Spielsprache | de, en | de | Sprache im Spiel. |
| Oberflächensprache | de, en | en | Sprache von MatchPulse. |
| Prüfungen pro Sekunde | 5 bis 10 | 10 | Wie oft das Bild geprüft wird. Weniger heißt weniger Last. |
| Match-Ende ohne Anzeige | Sekunden | 120 | Nach so langer Zeit ohne Spielanzeige gilt das Match als beendet. |
| Farbe der Kill-Markierung | Farbe (#RRGGBB) | Rot | Muss der Einstellung im Spiel entsprechen. |
| Diagnosemodus | an, aus | aus | Speichert Bilder von unklaren Fällen, siehe unten. |

## Diagnosemodus

Wenn der Spieler glaubt, dass ein Kill fehlt, schaltet er den Diagnosemodus ein. Dann speichert MatchPulse bei jedem unklaren Fall ein Bild in einen Ordner:

- Totenkopf ohne Kill-Markierung davor (zum Beispiel ein Kill des Squads oder ein Kill mit Granate),
- Kill-Markierung ohne Totenkopf danach (zum Beispiel ein Treffer ohne Kill oder ein Doppel-Kill).

Jedes Bild zeigt den Kill-Feed und die letzten 2,5 Sekunden um das Fadenkreuz. Heute muss der Spieler den Ordner selbst öffnen.

## Was die neue Oberfläche leisten sollte

Das sind Wünsche für das Design, keine fertigen Entscheidungen.

1. **Status auf einen Blick.** Spiel läuft oder nicht, Match läuft oder nicht, seit wann, wie viele Kills in dieser Runde.
2. **Letzte Ereignisse.** Eine Liste der letzten Game Events mit Uhrzeit und Art, Kills hervorgehoben. Klick zeigt das JSON.
3. **Zustand der Ziele.** Für jeden Webhook und jedes Skript: zuletzt erfolgreich, zuletzt fehlgeschlagen, Fehlermeldung. Ein Knopf „Test-Ereignis senden“.
4. **Einfache Einrichtung.** Webhooks und Skripte als Liste mit Hinzufügen, Bearbeiten, Entfernen statt als Textfeld. Farbe der Kill-Markierung mit Vorschau des Kreuzes.
5. **Diagnose ansehen.** Die gespeicherten Bilder in der Oberfläche durchblättern, mit Uhrzeit und Art des Falls, und den Ordner leeren.
6. **Log.** Das Protokoll lesbar anzeigen, mit Filter (nur Ereignisse, nur Warnungen) und automatischem Aktualisieren.
7. **Unaufdringlich.** Das Programm läuft beim Spielen im Hintergrund. Es darf nicht stören, keine Fenster in den Vordergrund holen und keine Töne machen. Optional eine kleine Windows-Benachrichtigung bei Problemen.
8. **Zwei Sprachen.** Alle Texte auf Deutsch und Englisch. Deutsche Texte sind oft länger.
9. **Hell und dunkel.** Passend zum Windows-Design. Spieler nutzen oft das dunkle Design.

## Rahmenbedingungen

- Windows 10 und 11, heute als WinForms-Programm in .NET umgesetzt.
- Typische Bildschirme: 1920×1080 bis 5120×2160, oft mehrere Monitore. Das Spiel läuft auf einem davon.
- Das Fenster ist meistens geschlossen. Das Symbol in der Taskleiste ist der wichtigste Berührungspunkt.
- Das Spiel heißt Battlefield 6. Name, Logo und Grafiken des Spiels gehören nicht in die Oberfläche von MatchPulse.

## Noch nicht unterstützt

- Runden im normalen Multiplayer (nur Kills werden dort erkannt).
- Weitere Ereignisse wie Knockdowns, eigener Tod oder Wiederbelebung.
- Weitere Ziele wie MQTT oder eine direkte OBS-Anbindung.
