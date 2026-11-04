# Datenschutz und Datenfluss

UseScope verarbeitet die konfigurierten Kontonamen, lokale Profilpfade sowie vom
Anbieter gelieferte Limits, Reset-Zeiten, Token, Kosten und Guthaben. E-Mail-Adressen können im
Widget angezeigt werden. Die Anwendung hat keine eigene Telemetrie, Werbung oder
Fernprotokollierung.

## Lokale Dateien

Unter Windows enthält `%LOCALAPPDATA%\UseScope\settings.json` die Einstellungen
und Kontoliste, auf dem Mac `~/Library/Application Support/UseScope/settings.json`.
Eine Sicherung der Einstellungen kann daneben als `.bak` liegen. Getrennte CLI-
Profile liegen normalerweise unter `profiles/`. Native Anbieterprogramme können
dort Zugangstokens und weitere Anmeldedaten speichern. UseScope kopiert diese
Tokens nicht in die Einstellungsdatei oder in seine Diagnoseberichte.

Eine optionale Claude-Tokenquelle wird nur nach bewusster lokaler Ordnerwahl und
Bestätigung ausgewertet. In den Einstellungen stehen dann der Ordner und ein
Hash der bestätigten Konto-/Organisationskennung zur Bindungsprüfung. Es werden
keine Sitzungstexte oder Prompts kopiert oder gespeichert. Der begrenzte
Arbeitsspeichercache enthält nur Messagehashes, Zeitstempel und Tokenzahlen.
Ohne passende Zuordnung wird keine automatische Suche in fremden CLI-Verläufen
gestartet; die Quelle lässt sich wieder entfernen.

Codex-Tokenaktivität wird über den dokumentierten `account/usage/read`-Bericht
der bestehenden eigenen CLI-Verbindung gelesen, nicht aus Browser- oder
Desktop-App-Sitzungen abgegriffen. Es wird kein neuer Browserlogin ausgelöst.

API-Schlüssel werden niemals in `settings.json` gespeichert. Windows schützt sie
mit DPAPI für den aktuellen Windows-Benutzer und das gewählte UseScope-Konto in
`profiles/<Konto-ID>/api-key.dpapi`. macOS verwendet Security.framework und den
Schlüsselbunddienst `io.github.zokahase-design.usescope.api`, Account
`<Anbieter>:<Konto-ID>`. Die xAI-Team-ID liegt mit dem Schlüssel in diesem geschützten
Eintrag. Schlüssel erscheinen nicht in Prozessargumenten, URLs oder Diagnoseberichten.
Sie müssen zum Senden der HTTPS-Anfrage kurz im Arbeitsspeicher vorliegen.

Mac-Hintergrundabfragen verlangen keine interaktive Schlüsselbundfreigabe. Bei
gesperrtem Schlüsselbund wird ein Fehler angezeigt; Schlüssel über die bewusste
Aktion in der Kontoverwaltung erneut hinterlegen. Schlüsselbund-Schreibzugriff und
native Berechtigungen müssen noch auf einem echten Mac abgenommen werden.

## Netzwerk und Prozesse

- Codex: UseScope startet einen lokalen Codex-App-Server und liest dessen Konto-
  und Limitantworten. Die Codex-CLI kann dabei mit ihrem Anbieter kommunizieren.
- Claude: bestehende OAuth-Daten aus dem isolierten Profil werden gelesen und
  als Bearer-Token ausschließlich an HTTPS-Endpunkte von `api.anthropic.com`
  gesendet. Bei aktivierter Erneuerung wird die native Claude-Code-CLI aufgerufen.
- Nous: bestehende Daten aus dem isolierten Profil werden für HTTPS-Anfragen an
  `portal.nousresearch.com` verwendet. Bei Bedarf erneuert die native Hermes-CLI
  ihre eigenen Token.

- API-Schlüssel werden ausschließlich an den fest hinterlegten HTTPS-Abfragehost
  des jeweiligen Adapters gesendet: `api.deepseek.com`, `api.moonshot.ai`,
  `openrouter.ai`, `api.openai.com` oder `management-api.x.ai`. Abfragen verwenden
  GET oder dokumentierte lesende POST-Abfragen und lösen keine Text-, Bild- oder Videogenerierung aus. Weitere feste Ziele: Anthropic Analytics, MiniMax PAYG, StepFun, Arcee, Mistral Admin, DigitalOcean Billing, Alibaba BSS, Google Monitoring und optional BigQuery. HTTP-Weiterleitungen
  mit Zugangsdaten werden nicht verfolgt.
- OpenAI-Admin-Abfragen liefern organisationsweite Abrechnungsdaten; OpenRouter
  liefert Daten zum hinterlegten Schlüssel, xAI zum angegebenen Team. API-Schlüssel
  können weitergehende Rechte haben als die hier ausgeführten Leseabfragen.
- Portal-Einträge öffnen auf Nutzeraktion den Standardbrowser. Cookies und
  Anmeldungen bleiben im Browser; UseScope liest diese nicht. Ein solcher Eintrag
  besitzt keine automatische Messung. Anbieterlogos werden lokal geladen.

Die privaten Claude-/Nous-Endpunkte können sich ändern. Die Anbieter und ihre
CLIs haben eigene Datenschutz- und Nutzungsbedingungen. UseScope verkauft keine
Kontodaten und sendet sie nicht an einen eigenen Server.

## Optionaler lokaler API-Zähler

Nur bei eingerichtetem Zähler startet ein Dienst auf `127.0.0.1:48371`, ausschließlich mit lokalem zufälligem Schlüssel je Konto. Die explizit gewählte öffentliche HTTPS-Anbieteradresse und der Inference-Key liegen im getrennten, geschützten Meter-Unterprofil. Admin-/Billing-Schlüssel werden nicht automatisch wiederverwendet. Die lokale Adresse enthält eine Konto-ID, keinen Schlüssel.

Dieser Modus leitet die **vom Nutzer im KI-Client ausgelösten** Modellanfragen weiter; dabei können beim Anbieter reguläre Kosten entstehen. UseScope selbst erzeugt oder wiederholt keine Modellanfragen. Nur ein bewusst gewählter Modelllisten-Test verwendet GET. Für den lokalen Modus werden Prompts und Antworten im Speicher durchgereicht; begrenzte Antwortfragmente dienen der Usage-Auswertung.

Im unverschlüsselten lokalen Journal `profiles/<Konto-ID>/meter/usage.jsonl` werden ausschließlich Zeit, HTTP-Status, gemeldete Token/Kosten, Abschlussstatus, ein Hash der Antwort-ID und gegebenenfalls dokumentierte IFM-Quotaheader gespeichert. Keine Prompts, Antworttexte, frei gelieferten Modellnamen, Cookies oder Schlüssel. Es gibt keine automatische Journal-Löschung; es wächst mit der Nutzung. Konto entfernen löscht den lokalen Schlüssel, nicht das Journal. Kopieren des lokalen Schlüssels ist eine ausdrückliche Nutzeraktion; danach liegt er in der systemweiten Zwischenablage. [Einrichtung und Grenzen](API_METER.md).

## Zusätzliche Kontozugänge

Muse, mmx, Bailian und gcloud arbeiten jeweils in einem eigenen UseScope-Profil. Umgebungsvariablen für fremde Schlüssel und API-Hosts werden vor dem Start bereinigt. Die offiziellen CLIs behalten ihre eigenen Datenschutzbedingungen. Ein Google-Billing-Export wird nur abgefragt, wenn eine konkrete bestehende Exporttabelle angegeben wurde: lesende SQL-Abfrage, höchstens alle 30 Minuten, Limit 100 MiB abgerechneter Daten pro Query. Es können BigQuery-Kosten entstehen; ohne Exporttabelle erfolgt keine solche Abfrage. Alibaba-Abrechnung kann das gesamte Cloudkonto betreffen und mit Qwen/Wan dieselbe Wallet teilen.

## Diagnosen

Die Offline-Prüfung `--check` verwendet erfundene Konten und temporäre Dateien.
Die gesonderte Windows-Diagnose `--probe` fragt dagegen konfigurierte Konten ab und
schreibt eine reduzierte Zusammenfassung. Reine Portale werden als übersprungen gekennzeichnet. Sie wird in CI und beim Paketbau nicht
ausgeführt. Diagnoseergebnisse vor einer Weitergabe prüfen.

## Entfernen

Bei **API-Konten** löschen sowohl „API-Schlüssel lokal löschen“ als auch das
Entfernen des Kontoeintrags den dafür gespeicherten DPAPI-/Schlüsselbundeintrag.
Der Schlüssel wird damit nicht beim Anbieter widerrufen; dies erfolgt im Anbieterportal.
Ein aus einem anderen Programm stammender Schlüssel wird dort nicht verändert.

Bei **CLI-Konten** löscht `Aus Anzeige entfernen` den Listeneintrag, nicht automatisch das lokale
Anmeldeprofil oder das Konto beim Anbieter. Zum Abmelden die entsprechende Aktion
verwenden. Zum vollständigen lokalen Entfernen UseScope beenden und den eigenen
Datenordner nach einer gegebenenfalls gewünschten Sicherung manuell entfernen.
Auf macOS entfernt das Löschen des Datenordners keine Schlüsselbundeinträge.
Vorher jedes Konto über die native Anbieter-CLI abmelden und die Abmeldung prüfen.
Verbleibende, zu diesen Profilen gehörende Einträge bei Bedarf in der
Schlüsselbundverwaltung selbst entfernen; keine fremden Kontoeinträge löschen.
