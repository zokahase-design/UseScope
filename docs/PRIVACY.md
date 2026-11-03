# Datenschutz und Datenfluss

UseScope verarbeitet die konfigurierten Kontonamen, lokale Profilpfade sowie vom
Anbieter gelieferte Limits, Reset-Zeiten und Guthaben. E-Mail-Adressen können im
Widget angezeigt werden. Die Anwendung hat keine eigene Telemetrie, Werbung oder
Fernprotokollierung.

## Lokale Dateien

Unter Windows enthält `%LOCALAPPDATA%\UseScope\settings.json` die Einstellungen
und Kontoliste, auf dem Mac `~/Library/Application Support/UseScope/settings.json`.
Eine Sicherung der Einstellungen kann daneben als `.bak` liegen. Getrennte CLI-
Profile liegen normalerweise unter `profiles/`. Native Anbieterprogramme können
dort Zugangstokens und weitere Anmeldedaten speichern. UseScope kopiert diese
Tokens nicht in die Einstellungsdatei oder in seine Diagnoseberichte.

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
  GET und lösen keine Text-, Bild- oder Videogenerierung aus. HTTP-Weiterleitungen
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
