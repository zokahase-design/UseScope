# UseScope starten

1. Dieses ZIP vollständig entpacken.
2. Im entpackten Ordner `UseScope starten.exe` öffnen.
3. Über das Tray-Menü ein Konto hinzufügen. Je nach Auswahl in der Anbieter-CLI anmelden, einen API-Schlüssel selbst hinterlegen oder das Anbieterportal öffnen.

Der Starter findet `runtime\UseScope.exe` relativ zu seinem eigenen Ordner.
Beide müssen in dieser Anordnung zusammenbleiben. Die Anwendung und ihre .NET-10-
Laufzeit liegen gemeinsam unter `runtime`. Bei Problemen mit dem kleinen Starter
kann `runtime\UseScope.exe` auch direkt geöffnet werden.

Für Codex, Claude und Nous muss die jeweilige Anbieter-CLI separat installiert sein. UseScope enthält keine
Codex-, Claude-Code- oder Hermes-Installation und keine vorbereiteten Konten.

Linksklick auf das Tray-Symbol oder `Strg+Alt+U` blendet das Widget ein und aus.
Rechtsklick öffnet Konten, Aktualisieren, Darstellung, Windows-Start und Beenden.
Am Schloss wird der Vordergrundmodus umgeschaltet. Das Listensymbol aktiviert
die Kontosortierung. Das Widget lässt sich an der Überschrift verschieben.

Vor einem Update UseScope selbst über das Tray-Menü beenden. Dann das neue Paket
vollständig entpacken. Einstellungen und Anmeldungen bleiben unter
`%LOCALAPPDATA%\UseScope`. Diesen persönlichen Ordner nicht weitergeben.

Claude und Nous verwenden private Kontoendpunkte. Diese können sich ändern und
unterliegen den Bedingungen der Anbieter. UseScope ist kein offizielles Produkt
von OpenAI, Anthropic oder Nous Research. Weitere Hinweise stehen in PRIVACY.md
und THIRD_PARTY_NOTICES.md. Die Anwendung steht unter der mitgelieferten MIT-Lizenz;
Fremdkomponenten behalten ihre gesonderten Lizenztexte.

Der Download ist nicht digital signiert. Die separat angebotene SHA-256-Prüfsumme
kann beim Erkennen einer beschädigten oder veränderten ZIP-Datei helfen.

API-Schlüssel werden benutzergebunden verschlüsselt gespeichert. API-Schlüssel lokal löschen oder den API-Eintrag entfernen löscht die lokale Kopie; es widerruft den Schlüssel nicht beim Anbieter.
