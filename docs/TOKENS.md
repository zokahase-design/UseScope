# Tokenanzeige

Rechts neben den gekürzten Limitbalken steht pro Konto eine kompakte Zahl mit Einheit,
z. B. `12,3k Token` oder `1,2M Token`. Breite und Höhe des Widgets bleiben erhalten.
Ein Token ist eine vom Anbieter gemeldete Verarbeitungseinheit, kein Wort und kein
direkter Geldbetrag. UseScope schätzt keine Token aus Prozentbalken, Credits oder Preisen.

Den genauen Wert, Zeitraum in UTC, Quelle und vorhandene Input-/Outputwerte
findest du unter „Werte und Verbindung erklären…“ bzw. „Infos“ im Kontomenü.
Beim Darüberfahren werden keine automatischen Infofenster eingeblendet.

## Quellen und Grenzen

| Verbindung | Erfasste Token | Zeitraum |
|---|---|---|
| Codex-Abo | `account/usage/read` auf derselben Codex-CLI-Verbindung | Wählbare UTC-Kalenderfenster aus vorhandenen Tageswerten (≥); Gesamt aus gemeldeter Gesamtsumme oder vorhandenen Tageswerten |
| Claude-Abo mit lokaler Tokenquelle | Ausdrücklich zugeordnete Claude-Code-JSONL-Dateien einschließlich Cache | Wählbare Kalender-/rollierende Fenster, Teilmessung |
| OpenAI API-Organisation | Text-/Completion-Input und -Output; andere Produkte können fehlen | Laufender UTC-Monat bis zum abgefragten Zeitpunkt |
| Anthropic API-Organisation | Input einschließlich Cache und Output | Laufender UTC-Monat, abgeschlossene Stunden; Anbieterstand beachten |
| Arcee API | Vom Kontobericht gelieferte Input-/Output-/Gesamttoken | Laufender UTC-Monat |
| Google-Projekt | Nur gemeldete Gemini-Outputtoken, keine erfundenen Inputtoken | Laufender UTC-Monat, abgeschlossene Stunden |
| Lokaler API-Zähler | Gemeldete Token aus durch UseScope geleiteten Anfragen | Wählbare Kalender-/rollierende Fenster, nur vorhandene Aufzeichnungen |

## Zeitraum auswählen

Windows: Tray-Menü → Konten → Konto → **Tokenzeitraum**. Mac: Konten →
**Tokenzeitraum**. Die Auswahl gilt je Konto und bleibt nach Neustart erhalten.
`Anbieterstandard` erhält das bisherige Verhalten: Codex laufender UTC-Monat,
falls passende Tageswerte vorliegen, sonst Gesamt; lokale Quellen laufender UTC-Monat.

Codex bietet **Heute**, **letzte 7 Kalendertage** (heute und sechs vorherige
UTC-Tage), **laufender Monat**, **Vormonat** und **Gesamt**. Die Kalenderoptionen
sind ohne Tageswerte deaktiviert. Eine gespeicherte Auswahl ohne inzwischen
passende Daten zeigt eine Erklärung statt einer fremden Gesamtsumme oder Null.
Nicht gemeldete Tage bleiben unbekannt, auch innerhalb eines gewählten Zeitraums.
Codex liefert laut [App-Server-Dokumentation](https://learn.chatgpt.com/docs/app-server)
optionale Tageswerte und eine optionale Gesamtsumme; daraus werden keine
stundengenauen Werte abgeleitet.

Lokale Claude-Code-Dateien und der API-Zähler bieten zusätzlich **letzte 24
Stunden** und **letzte 7 × 24 Stunden**. Vormonat endet ausschließlich vor dem
ersten Zeitstempel des aktuellen UTC-Monats. Gesamt bedeutet hier nur vorhandene
lokale Aufzeichnungen, niemals rückwirkend vollständiger Kontoverbrauch.
Fehlen bei Claude sämtliche passenden erkennbaren Meldungen, bleibt der Wert unbekannt.

Die Auswahl wertet vorhandene Berichte bzw. lokale Dateien neu aus. Sie setzt
keine Abfragepausen zurück und benötigt keine zusätzliche Anbieteranfrage.
Guthaben, Monatskosten und Limitfenster behalten ihre ursprünglichen Zeiträume.
Andere direkte API-Adapter bieten weiterhin ihren festen Berichtszeitraum.

`≥` bedeutet: mindestens diese gemeldete Menge; die Messung deckt das Konto oder
den Zeitraum nicht vollständig ab. Beim lokalen API-Zähler fehlen andere Apps,
frühere nicht durchgeleitete Nutzung und ggf. Antworten ohne Tokenmeldung.
Google meldet hier nur Output; OpenAI-Textnutzung umfasst nicht alle Produktarten.
Ein verzögerter Anthropic-Snapshot wird ebenfalls als unvollständig gekennzeichnet.

`Token n. v.` bedeutet nicht verfügbar. Dazu gehören Verbindungen, die nur
Guthaben oder Limitprozente melden, und Tokenabfragen ohne verwertbare Daten.
Die reine Abo-Limitabfrage liefert keine Tokenzahl. Codex nutzt deshalb den
gesonderten, dokumentierten Kontobericht auf derselben lokalen Verbindung,
maximal alle 15 Minuten. Eine ältere CLI oder ein fehlender Anbieterbericht kann
weiterhin unbekannte Werte liefern. Die Organisations-APIs sind davon getrennte API-Produkte.
Eine echte gemeldete Null bleibt `0 Token`. Eine lokale Null bezieht sich nur auf
den lokalen Zähler und erscheint wegen der Kontolücke als `≥ 0 Token`.

Bei einem Abfragefehler bleiben vorhandene Tokenwerte als letzter bekannter Stand
erhalten und werden gedimmt dargestellt; die Details nennen den Fehlerzustand und
den ursprünglichen Zeitraum. Eine erneute Anmeldung verwirft alte Kontowerte.

Es werden für die Anzeige keine zusätzlichen kostenpflichtigen Generierungen
ausgelöst. Die Anbieterabfragen verwenden die gedrosselten Abfrageintervalle;
die lokale Dateiauswertung benötigt keine zusätzliche Anbieteranfrage.

## Claude: lokale Tokenquelle einmalig zuordnen

Auf `Tokenquelle…` im Widget klicken oder im Kontomenü `Lokale Tokenquelle…`
wählen. Nach einer erfolgreichen Kontoverbindung einen getrennten lokalen
Claude-Code-Sitzungsordner mit JSONL-Dateien auswählen und die Zuordnung bestätigen.
Der Ordner darf nur Sitzungen dieses Kontos enthalten: Die Dateien selbst erlauben
keine garantierte automatische Kontozuordnung. Gemeinsame Verlaufsordner mehrerer
Anmeldungen dürfen nicht pauschal einem einzelnen Konto zugerechnet werden.

Die Auswahl wird an die bestätigte Konto-/Organisationskennung gebunden. Bei
anderer oder nicht bestätigter Anmeldung wird die Auswertung gesperrt, bis die
Quelle bewusst neu zugeordnet ist. Überlappende Ordner für verschiedene Konten,
Netzlaufwerke, Symlinks und umgeleitete Elternordner werden nicht angenommen.

Es entstehen dafür keine Anbieteranfragen. Es werden nur Tokenfelder, Messagehash
und Zeit ausgewertet, keine Prompts oder Antworten kopiert oder gespeichert.
Streaming-Snapshots und doppelte Nachrichten werden nicht mehrfach summiert.
Beim Monatswechsel wird ein schon zuvor gemeldeter Anteil nicht erneut gezählt.
Leere/unbekannte Formate liefern keine erfundene Null. Dateimengen und Lesezeit
sind begrenzt; ein kleinerer, passend zugeordneter Sitzungsordner kann nötig sein.

Lokale Werte werden bei laufendem Tool höchstens alle 15 Sekunden aktualisiert,
auch wenn die Anbieterabfrage gerade pausiert. Claude-Webnutzung, andere Geräte
und nicht im gewählten Ordner erfasste Sitzungen fehlen. Daher steht sichtbar `≥`.
Die Quelle lässt sich im Kontomenü wieder entfernen. UseScope konfiguriert keine
fremde CLI automatisch und durchsucht keine Standardprofile ohne diese Auswahl.
