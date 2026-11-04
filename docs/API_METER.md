# Lokaler API-Zähler

Der Zähler misst nur Anfragen, die dein KI-Client durch UseScope leitet. Historischer Verbrauch, andere Apps und der Rest deines Kontos werden dadurch nicht sichtbar. Er ersetzt weder Anbieterrechnungen noch Kostenlimits beim Anbieter.

## Einrichten

1. **Konto hinzufügen** und den tatsächlichen API-/Hosting-Anbieter auswählen. Falls ein direkter Kontoabruf verfügbar ist, kann daneben ein eigener Eintrag mit **Lokaler API-Zähler** angelegt werden.
2. Einen **Inference-Key** eintragen. Admin-/Billing-Schlüssel sind dafür ungeeignet. Bei festen Endpunkten sind Adresse und Auth-Header vorbelegt; eigene Region-/Ressourcenadressen aus der Herstellerdokumentation übernehmen. Für unbekannte kompatible Anbieter gibt es **Eigener API-Endpunkt**.
3. **API-Adresse und lokalen Schlüssel** öffnen und beides über die Kopierschaltflächen im KI-Client hinterlegen. Der lokale Schlüssel gehört nur zu diesem UseScope-Konto. Den Anbieter-Key erhält der Client nicht.
4. **API-Verbindung ohne Generierung prüfen** ruft nur `/models` ab. Nicht alle Anbieter bieten diesen Leseweg; ein 404/405 bestätigt keine funktionierende oder defekte Generierung. Die erste reguläre, bewusst von dir ausgelöste Anfrage ist der praktische Verbindungstest.
5. UseScope während der API-Nutzung laufen lassen. Zur direkten Anbieteradresse zurückwechseln, wenn UseScope beendet werden soll.

OpenAI-kompatible Clients übernehmen die angezeigte Adresse einschließlich `/v1`. Native Anthropic-/Cohere-Clients verwenden die Adresse bis zur Konto-ID und hängen selbst `/v1/messages` bzw. `/v2/chat` an. Native Gemini-Clients verwenden ebenfalls die Kontobasis und die API-Version `v1beta`; bei manuellen URLs endet der Pfad beispielsweise auf `/v1beta/models/<Modell>:generateContent`. UseScope übersetzt keine Request-Protokolle oder Modellnamen.

## Was gezählt wird

- Kalendermonat in **UTC**, je eingerichtetem Konto: weitergeleitete POST-Versuche, erfolgreich abgeschlossene Antworten und vom Anbieter gemeldete Token. GET-Modelllisten zählen nicht als Nutzung.
- OpenAI Chat/Responses, Anthropic Messages, Gemini und Cohere besitzen eigene Parser. Wiederholte kumulative Streaming-Meldungen werden nicht addiert. Bei gleicher Anbieter-Response-ID werden Token/Kosten zusammengeführt; ohne solche ID lässt sich ein providerseitig idempotenter Wiederholungsversuch nicht sicher erkennen.
- Fehlende Usage bleibt **unbekannt**. Bei Streams nach Möglichkeit `stream_options.include_usage` im Client aktivieren. Ein Abbruch kann bereits berechnete Token ohne finale Meldung hinterlassen. Teilzahlen sind keine vollständige Rechnung.
- Kosten werden nur aus ausdrücklich bekannten Anbieterfeldern übernommen: OpenRouter meldet USD; xAI meldet USD-Ticks mit 10 Milliarden Ticks pro USD. Für andere Antworten werden keine Modellpreise angenommen. Fehlende Kosten sind keine kostenlosen Anfragen.
- IFM kann sein Tageskontingent in dokumentierten Antwortheadern melden. Dieses Fenster zeigt den letzten empfangenen Stand mit Reset um 00:00 UTC; es ist keine unabhängige periodische Kontoabfrage.
- Text-, Embedding-, Bild- und Audio-Routen können weitergeleitet werden. Bei Medien sind Anfragen zählbar, Token oder Kosten nur bei passender Usage-Meldung. Asynchrone Video-/Training-/Batch-Jobs und deren spätere Abrechnung sind nicht pauschal angebunden.

## Daten und Grenzen

Der lokale Dienst bindet ausschließlich `127.0.0.1:48371`. Er verlangt den zufälligen lokalen Kontoschlüssel. Ziele müssen öffentliche HTTPS-Adressen sein; private Zieladressen, Redirects, zweideutige Pfade und Verwaltungsrouten werden blockiert. Es gibt keine CORS-Freigabe für Browserseiten. Es werden nur benötigte Auth-, Protokoll- und Rate-Limit-Header sowie die Query-Felder `alt` und `api-version` übertragen.

Anfragen/Antworten werden durch den Speicher gestreamt. Für die Usage-Auswertung bleiben höchstens begrenzte Antwortfragmente im Speicher. Auf Disk landen nur Zeit, Status, Token, bekannte Kosten, Abschlussstatus und ein Hash der Response-ID. **Keine Prompts, Antworttexte, Cookies oder Schlüssel im Nutzungsjournal.**

Schlüssel liegen getrennt von Kontoadmin-Zugängen im DPAPI-/Keychain-Unterprofil. Das unverschlüsselte Metadatenjournal liegt unter `profiles/<Konto-ID>/meter/usage.jsonl`. Es wächst mit der Anzahl der Anfragen und wird nicht automatisch gelöscht. Bei typischen Einträgen sind 100.000 Anfragen einige zehn MB; die tatsächliche Größe hängt von den Metadaten ab. Eine lokale Datensicherung kann dieses Journal enthalten. Nicht veröffentlichen.

Schlüssel löschen oder Konto entfernen beendet neue Verbindungen; eine bereits laufende Anfrage kann noch abschließen. Das lokale Journal bleibt erhalten. Der Anbieter-Key wird dadurch beim Hersteller nicht widerrufen. Ein belegter lokaler Port wird als Fehler angezeigt. Andere lokale Programme und die Zwischenablage gehören zur Vertrauensgrenze deines Computers.

Maximal 16 parallele weitergeleitete Anfragen, 64 MiB Request-Body, 30 Minuten pro Anfrage. UseScope wiederholt fehlgeschlagene Generierungen nicht selbst. API-Clients können eigene Retries ausführen; deren Einstellungen gelten weiterhin.

Alle Protokolltests arbeiten mit erfundenen Daten und lokalen HTTP-Gegenstellen. Sie bestätigen keine echten Anmeldungen, Tarifrechte oder Anbieterbedingungen. [Anbietermatrix](PROVIDER_MATRIX.md).
