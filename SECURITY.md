# Sicherheit

Bitte keine Zugangsdaten, Tokens oder unbereinigten Profildateien öffentlich
melden. Auch `settings.json`, lokale Pfade und Screenshots können persönliche
Angaben enthalten.

Für ein vertrauliches Problem den Maintainer über sein GitHub-Profil kontaktieren
und zunächst nur eine allgemeine Beschreibung senden. Die private GitHub-
Sicherheitsmeldung kann nach Einrichtung des Repositorys freigeschaltet werden.
Solange kein privater Kanal bestätigt ist, keine sensiblen Details in ein Issue
oder einen Pull Request schreiben. Ein bereits offengelegtes Token beim Anbieter
widerrufen.

UseScope liest vorhandene lokale CLI-Anmeldungen und verwendet sie für
Anbieterabfragen. Diese Profile wie Passwörter behandeln. UseScope bietet keine
Fernwartung. Der optionale lokale API-Zähler startet nach bewusster Einrichtung
einen ausschließlich an `127.0.0.1` gebundenen Loopback-Dienst. Jede Anfrage
benötigt den getrennten lokalen Kontoschlüssel; Origin-/Hostprüfungen und eine
feste Routenliste begrenzen den Zugriff. Diesen Schlüssel nicht weitergeben.

Repository und Git-Historie werden in CI mit Gitleaks geprüft. Downloads enthalten
keine vorbereiteten Konten oder Zugangsdaten. Nur geprüfte ZIPs aus `dist` verwenden;
den persönlichen Datenordner und lokale Nutzungsjournale niemals mitverteilen.

Der Download ist derzeit nicht digital signiert. Prüfsummen helfen beim Erkennen
beschädigter oder veränderter Dateien, ersetzen aber keine Herausgebersignatur.
Windows kann deshalb einen Hinweis auf einen unbekannten Herausgeber anzeigen.
Schutzfunktionen nicht pauschal deaktivieren.
