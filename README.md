![UseScope – Limits, Guthaben, Reset-Zeiten. Deine KI-Konten im Blick.](docs/images/usescope-cover.png)

# UseScope

Ein kompaktes Desktop-Widget für die Nutzungslimits deiner KI-Konten: mehrere
Anmeldungen, Reset-Zeiten und gemeldetes Guthaben in einer kompakten Ansicht.

**Windows-Version und macOS-Vorschau.** Die Mac-Pakete sind getrennt für Apple
Silicon und Intel vorbereitet. Ihr echter macOS-Funktionstest sowie Signierung und
Notarisierung stehen noch aus; siehe [Mac-Prüfstand](docs/MACOS.md).

## Herunterladen und starten

1. Unter **[Releases](../../releases)** die Datei `UseScope-2.1.0-win-x64.zip` herunterladen.
2. Das **gesamte ZIP entpacken**, beispielsweise nach `Dokumente\UseScope`.
3. `UseScope starten.exe` öffnen. Zum Beenden das Tray-Menü verwenden.

Auf dem Mac das zum Prozessor passende `osx-arm64-preview`- oder
`osx-x64-preview`-ZIP wählen und die enthaltene `UseScope.app` verwenden.

Der Download enthält die .NET-10-Laufzeit. Eine separate .NET-10-Installation ist
zum Benutzen nicht erforderlich. EXE und DLLs bleiben zusammen im Unterordner
`runtime`. Bitte das Programm nicht direkt aus der ZIP-Datei starten.

**Plattform:** Windows 11, x64. Andere Windows-Versionen und ARM64 sind nicht
abgenommen. Der kleine Starter nutzt die zu Windows gehörende .NET-Framework-4.x-
Laufzeit. Bei deaktivierter Windows-Komponente direkt `runtime\UseScope.exe` öffnen.

Die GitHub-Schaltfläche **Code → Download ZIP** liefert den Quellcode, nicht den
startfertigen Windows-Download.

## Was UseScope anzeigt

| Verbindung | Automatische Anzeige | Zugang |
|---|---|---|
| OpenAI Codex | Abo-Nutzungsfenster, Reset-Zeiten, gemeldete Credits | Offizielle Codex-CLI |
| Anthropic Claude | Abo-Nutzungsfenster, gemeldetes Prepaid-Guthaben | Claude Code; private Kontoendpunkte |
| Hermes / Nous Portal | Gemeldetes Nous-Kontoguthaben | Hermes-CLI; privater Kontoendpunkt |
| DeepSeek API | API-Guthaben in gemeldeter Währung | API-Schlüssel |
| Kimi / Moonshot International | API-Guthaben in USD | Internationaler API-Schlüssel |
| OpenRouter | Verbrauch dieses Schlüssels und verbleibendes Ausgabenlimit | API-Schlüssel |
| OpenAI API | Organisationskosten sowie Text-Input/Output-Token im laufenden UTC-Monat | Admin-API-Schlüssel |
| xAI / Grok API | Vorzeichenbehafteter API-Teamsaldo in USD | Management-Key und Team-ID |
| Grok/SuperGrok, Gemini, Qwen, Wan, MiniMax, Meta Muse, Anthropic API | **Portalzugang; keine automatische Messung in dieser Version** | Offizielles Anbieterportal |

**Abo, API-Konto und Modell sind unterschiedliche Dinge.** Ein Chat-Abo ist kein
API-Guthaben. Kimi K3, Muse Image/Wonder, Wan 3.0 oder MiniMax H3 werden dort
abgerechnet, wo du sie verwendest. Wenn du verschiedene Modelle über OpenRouter
nutzt, zeigt dessen Verbindung den Verbrauch des hinterlegten Schlüssels. Andere
API-Aufrufe werden dadurch nicht automatisch mitgezählt.

UseScope schätzt keine historischen Kosten aus aktuellen Modellpreisen und leitet
keinen Tokenverbrauch aus sinkendem Guthaben ab. API-Anbieter liefern teilweise
nur Guthaben, Kosten oder einen bestimmten Ausschnitt der Nutzung. Fehlende Werte
bleiben `—`; eine leere Kostenausgabe wird als fehlende Meldung angezeigt.
OpenRouter-Restlimit und xAI-Teamsaldo sind ausdrücklich kein normalisiertes
Kontoguthaben. Datenstand und Umfang stehen bei **Werte und Verbindung erklären…**
(Windows) bzw. **Infos** in der Mac-Kontoverwaltung.

[Verfügbare Daten, offizielle Quellen und Prüfgrenzen](docs/PROVIDERS.md)

<table>
  <tr><th>Dunkel</th><th>Hell</th></tr>
  <tr><td><img src="docs/images/dark.png" alt="UseScope in Dunkel mit erfundenen Beispielkonten"></td><td><img src="docs/images/light.png" alt="UseScope in Hell mit erfundenen Beispielkonten"></td></tr>
</table>

Alle Konten und Zahlen in diesen Bildern sind Beispieldaten. Anbieterlogos werden
lokal mitgeliefert; [Bildquellen](docs/BRAND_ASSETS.md).

<img src="docs/images/api-dark.png" width="400" alt="API-Guthaben, Schlüsselverbrauch und als Portal gekennzeichnete Anbieter mit erfundenen Beispieldaten">

## Konten verbinden

Im Tray-Menü **Konto hinzufügen…** wählen. Die Auswahl erklärt vorab den Zugangsweg:

- **Codex, Claude und Nous:** Die jeweilige offizielle CLI separat installieren
  ([Codex](https://developers.openai.com/codex/cli/),
  [Claude Code](https://code.claude.com/docs/en/setup),
  [Hermes](https://hermes-agent.nousresearch.com/docs/)). Die CLI öffnet zur Anmeldung
  den Browser; der Terminalschritt bleibt Teil dieses offiziellen Verfahrens.
  Jedes UseScope-Konto hat ein eigenes Profil. Andere Anmeldungen werden nicht
  automatisch übernommen.
- **API-Verbindung:** Über die Schaltfläche das offizielle Portal öffnen, dort den
  passenden Schlüssel erstellen und selbst in UseScope eintragen. OpenAI benötigt
  einen Admin-Key, xAI einen Management-Key und die Team-ID. Unter Windows wird der
  Schlüssel benutzergebunden verschlüsselt; auf dem Mac im Schlüsselbund gespeichert.
  Eine Browseranmeldung allein überträgt keine Abrechnungsberechtigung an UseScope.
- **Portal:** Öffnet die offizielle Anbieterwebsite. Das Konto kann als Eintrag mit
  dem Anbieterlogo geführt werden; UseScope meldet es ausdrücklich als Portal ohne
  automatische Messung. Hierfür werden keine Zugangsdaten erfasst.

Anmeldeabbrüche und fehlende Rechte ergeben einen Verbindungsfehler. Bei Problemen
CLI-Installation beziehungsweise Schlüsseltyp und Berechtigung prüfen. Keine
Zugangsdaten in Issues oder Screenshots posten. Die automatisierten Tests ersetzen
keine echte Erstanmeldung bei jedem Anbieter oder einen Test auf einem echten Mac.

## Bedienung

- **Tray-Linksklick** oder `Strg+Alt+U`: Widget anzeigen / ausblenden.
- **Tray-Rechtsklick**: aktualisieren, Konten, Darstellung, Windows-Start und Beenden.
- **Schloss**: Widget im Vordergrund halten.
- **Listensymbol**: Sortiermodus aktivieren und Konten an die gewünschte Stelle ziehen.
- **Überschrift ziehen**: Widget verschieben. Zum Arbeitsbereich bleiben 16 Pixel Abstand.
- **Escape**: Widget ausblenden.

**Aus Anzeige entfernen** löscht bei API-Konten auch den lokal für UseScope gespeicherten
Schlüssel. Es widerruft ihn nicht beim Anbieter. Bei CLI-Konten bleiben lokale
Anmeldungen erhalten; falls gewünscht vorher **Abmelden** verwenden.

## Daten und Sicherheit

Einstellungen und getrennte Anmeldungen liegen unter `%LOCALAPPDATA%\UseScope`.
Sie gehören nicht zum Download oder zum Repository. Anmeldeprofile können sensible
Tokens enthalten. Den persönlichen Datenordner niemals hochladen oder weitergeben.

UseScope hat keine eigene Telemetrie. Abfragen gehen an die jeweils angezeigten
Anbieter. Die Anbieter-CLIs und deren eigene Einstellungen bleiben eigenständig.
Mehr dazu: [Datenschutz und Datenfluss](docs/PRIVACY.md).

**Claude und Nous verwenden private Kontoendpunkte.** Diese sind nicht als stabile
öffentliche API zugesichert. Ihre technische Verfügbarkeit ist keine Bestätigung,
dass jede Verwendung nach den jeweiligen Anbieterbedingungen erlaubt ist.
UseScope ist kein offizielles Produkt von OpenAI, Anthropic oder Nous Research.

## Aktualisieren

UseScope über das Tray-Menü beenden. Das neue Release vollständig in einen
Programmordner entpacken. Die Daten unter `%LOCALAPPDATA%\UseScope` bleiben bestehen.
Beim ersten normalen Start wird ein aktivierter Windows-Start auf den aktuellen
Programmpfad angepasst. Vor einem manuellen Löschen des Datenordners eine lokale
Sicherung anlegen; dabei keine Anmeldeprofile veröffentlichen.

## Aus dem Quellcode bauen

Windows, PowerShell 7 und das [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) werden benötigt.
PowerShell im Repository öffnen und ausführen:

```powershell
.\tools\build.ps1
.\tools\package.ps1
```

Der Build und die Offline-Prüfungen landen unter `artifacts/`, das fertige ZIP unter
`dist/`. Die Tests verwenden ausschließlich erfundene Konten und benötigen keine
Anmeldung. Sie öffnen kein sichtbares Widget. Der Workflow baut und prüft, er
veröffentlicht keine Releases automatisch.

Für macOS zusätzlich `tools/build-mac.ps1` ausführen (Python 3 erforderlich).
Dies erstellt beide Prozessorarchitekturen. Der separate Mac-Workflow prüft nach
dem Upload zusätzlich den nativen Offline-Start, veröffentlicht aber ebenfalls nichts.

## Mitwirken

Fehlerberichte und Pull Requests sind willkommen. Bitte vorab
[CONTRIBUTING.md](CONTRIBUTING.md) lesen. Sicherheitsprobleme und Tokens nicht
öffentlich posten; siehe [SECURITY.md](SECURITY.md).

## Lizenz und Marken

Die Anwendung wird unter der [MIT-Lizenz](LICENSE) bereitgestellt. Sie erlaubt auch
Änderungen, Weitergabe und kommerzielle Nutzung. Die mitgelieferte .NET-Laufzeit hat
eigene [Lizenzhinweise](THIRD_PARTY_NOTICES.md). Die MIT-Lizenz überträgt keine
Markenrechte an Anbieterlogos oder Anbieternamen.
