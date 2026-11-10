# Änderungen

## 2.6.2 — breitere Limitbalken

- Breitere Limitbalken bei gleicher Widget-Größe unter Windows und macOS.
- Tokenzahlen und Zeitraumwahl aus Widget und Kontomenüs entfernt. Das optionale lokale API-Journal und vorhandene Kontoeinstellungen bleiben erhalten. Früher verknüpfte Claude-Verläufe lassen sich weiterhin im Kontomenü trennen.

## 2.6.1 — Windows-Widget nach vorne holen

- Ein verdecktes Widget mit offenem Schloss lässt sich per Tray-Klick, Tastenkürzel oder **Widget nach vorne holen** wieder anzeigen. **Widget ausblenden** ist eine eigene Aktion.
- Das Fenster wartet mit dem Anheben, bis offene Tray-Menüs geschlossen sind.

## 2.6.0 — geordnetes Tray-Menü und Veröffentlichungsvorbereitung

- Tray-Menü nach Schnellaktionen, Konten, Darstellung und Programmende gegliedert. Konto-Untermenüs gruppieren Werte/Aktualisierung, Anzeige/Tokenquelle, Verbindung und Verwaltung. Der doppelte Portalaufruf entfällt.
- Schutz gegen geerbte API-Schlüssel und Hostüberschreibungen unter Windows unabhängig von Groß-/Kleinschreibung. Der lokale Zähler schreibt keine vom Anbieter gelieferten Modelltexte mehr ins Journal.
- Tokenzeitraum je Quelle/Konto, umschaltbare Verbrauchs-/Restkontingentbalken und direkte Plus-Schaltfläche; keine automatischen Infofenster. Resetzeiten und fehlende Angaben bleiben unterscheidbar.
- Aktualisiertes Marketingcover mit fiktiven Konten und Betriebssystemsymbolen. Sicherheits-/Datenschutzdokumentation erläutert den optionalen Loopback-Dienst und lokale Journalgrenzen.
- Secret-Scanning für Quellbaum und Git-Historie; Datenschutzprüfung der Windows-/Mac-Pakete. Mac-CI prüft ARM64 und Intel auf jeweils passender Hardware. Diese Offlineprüfungen ersetzen keine interaktive Kontoabnahme oder Herausgebersignatur.

## 2.5.2 — keine automatischen Infofenster

- Hover-Infofenster im Widget, an Schaltflächen und in Menüs vollständig entfernt (Windows und Mac).
- Kontodetails bleiben ausdrücklich über „Werte und Verbindung erklären…“ bzw. „Infos“ abrufbar. Anklickbare Tokenquelle und Kontosortierung bleiben erhalten.

## 2.4.0 — ruhigere Abfragen und native Tokenquellen

- Konten pro Anbieter werden nacheinander abgefragt, mit 15 Sekunden Startabstand und höchstens zwei Anbieterabfragen insgesamt. Native Konten frühestens alle fünf Minuten, API-Abrechnungen alle zehn Minuten, manuelle Wiederholungen frühestens nach einer Minute. Anbieterpausen werden respektiert; Fehler führen zu längeren Pausen.
- Codex nutzt einen wiederverwendeten lokalen Appserver je Konto und dessen Tokenaktivitätsbericht. Tokenfehler nehmen erfolgreiche Limitwerte nicht mit herunter; Zeitraum und unvollständige Werte bleiben sichtbar.
- Claude startet zur Statusprüfung keine MCP-Serverliste mehr. Nur geänderte Anmeldedaten werden nach einem 401 erneut versucht; fehlende oder bekannte abgelaufene Anmeldung wird klar angezeigt. Guthabencaches werden bei Kontowechsel verworfen.
- Optionale lokale Claude-Code-Tokenquelle mit expliziter Ordnerzuordnung, Anmeldebindung, Mengen-/Zeitgrenzen und Streaming-Deduplizierung. Direkt über Tokenspalte oder Kontomenü erreichbar; kein automatisches Lesen globaler Verläufe.
- Ausdrückliche leere Plan-/Konfigurationsantworten beleben keine alten Limits wieder. Fremde CLI-Exitcodes werden ohne Beleg nicht als Authentifizierungsfehler klassifiziert.

## 2.3.0 — Tokenzähler im Widget

- Tokenzahl je Konto rechts neben schmaleren Limitbalken; Fensterabmessungen bleiben gleich. Exakter Wert, Zeitraum und Messumfang in den Details.
- Bestehende Tokenquellen aus API-Berichten und lokalem API-Zähler strukturiert angebunden. Teilmessungen sichtbar mit ≥, fehlende Angaben als nicht verfügbar.
- Letzte Tokenwerte bleiben bei Abfragefehlern als veraltet erhalten, werden bei erneuter Anmeldung aber verworfen. Alter Anthropic-Snapshot erzeugt zum Monatswechsel keine falsche Null.

## 2.2.3 — Konten direkt hinzufügen und eindeutige Werte

- Plus-Schaltfläche oben rechts im Widget öffnet den vorhandenen Hinzufügen-Prozess unter Windows und macOS.
- Echte Null-Credits werden auch aus numerischen Codex-Antworten übernommen. Credits erscheinen mit Einheit, abgelaufene Resetzeiten als 0h 0min.
- Fehlende Guthaben-/Resetangaben werden verständlich benannt. Unbekannte Daten werden nicht als gemessene Null ausgegeben.

## 2.2.2 — Tray und Programmstart

- UseScope und Versionsnummer als lesbarer, nicht anklickbarer Menükopf; keine Tray-Hover-Infoeinblendung.
- Windows-Trayübersicht und eigenes Kontextmenü setzen die Vordergrundpriorität gemeinsam vorübergehend aus. Unveränderte Priorität wird bei Datenaktualisierungen nicht erneut gesetzt.
- Fester Starter für lokale Installationen und Prüfung auf bereits laufende UseScope-Versionen aus einem anderen Ordner. Autostart verwendet den festen Starter, sofern vorhanden.

## 2.2.1 — Tray-Menü

- Versionsnummer direkt neben UseScope im Tray-/Menüleistenmenü, automatisch aus der gebauten Anwendung gelesen.
- Auswahlzeilen, gedrückte Menüpunkte und Schrift im Windows-Menü verwenden die App-Farben. Keine hellblaue Systemmarkierung im dunklen Schema; Windows-Hochkontrast bleibt berücksichtigt.

## 2.2.0 — vorbereiteter Prüfstand

- 53 Anbieterfamilien mit explizitem Messumfang und lokalen Logos; 46 API-Zähler-Presets und 39 feste Endpunkte. Eigene Ressourcenadressen werden ausdrücklich angefordert.
- Neue direkte Adapter für Anthropic Analytics, Muse, MiniMax-Plan/PAYG, Google-Projekte, Alibaba-Abrechnung/Pläne, Arcee, StepFun, Mistral Enterprise und DigitalOcean Cloud Billing.
- Optionaler API-Zähler auf Loopback, getrennte geschützte Inference- und lokale Schlüssel, JSON-/SSE-Messung, keine Prompt-/Antwortprotokollierung. Bekannte Kostenfelder und IFM-Quotaheader werden ausgewertet; keine Preisschätzung.
- Native Windows-/Mac-Konfiguration, Kontolöschung, Client-Zugang und Modelllisten-Verbindungstest. ASP.NET-Core-Laufzeit und deren Lizenztexte ergänzt.
- Offline-Protokoll-/Negativtests sind vom Echtkontotest getrennt. Kein automatischer öffentlicher Release; native macOS-Abnahme und Anbieter-/Rechteprüfung weiterhin erforderlich.


## 2.1.0

- API-Anbindungen für DeepSeek, Kimi International, OpenRouter und OpenAI-Organisationen.
- xAI-Team-Abrechnung mit Management-Key und expliziter Team-ID.
- Offizielle Portale für Grok, Gemini, Qwen, Wan, MiniMax, Meta Muse und Anthropic API; klar als nicht automatisch gemessen gekennzeichnet.
- Anbieterlogos, Verbindungshinweise sowie getrennte Darstellung von Guthaben, Budget und Kosten.
- API-Schlüssel über Windows-DPAPI oder macOS-Schlüsselbund; lokale Löschung beim Entfernen eines API-Kontos.
- Windows-CLI-Anmeldung prüft den Rückgabecode. Zusätzliche Offline-Prüfungen für Authentifizierung, Fehler, Beträge und Pagination.

## 2.0.2

- Kompakte Übersicht für mehrere getrennte KI-Konten.
- Hell/Dunkel, Konto-Reihenfolge und optionale Guthabenanzeige.
- Kontextmenü bleibt bei Hintergrundaktualisierungen vor dem Widget.
- Automatische Bildschirmkorrektur mit 16 Pixel Abstand zur Taskleiste.
- Abfragekoordination bei Anmeldung, Abmeldung und Wiederverbindung.
- Offline-Prüfungen und portable Windows-Paketierung.

Die Versionsnummer orientiert sich am aktuellen Anwendungsstand.
