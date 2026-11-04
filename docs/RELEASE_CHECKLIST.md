# Abnahme vor öffentlichem Release

Ein erfolgreicher Offline-Build ersetzt weder eine echte Anmeldung noch die Freigabe zur Veröffentlichung. Build-Workflows veröffentlichen keine Releases automatisch.

Vor einem öffentlichen Release sind Paketintegrität, interaktive Windows-/Mac-Abnahme, veröffentlichter Messumfang, Rechte/Anbieterbedingungen und ausdrückliche Freigabe des Verantwortlichen erforderlich. Die Mac-Pakete bleiben ohne native Abnahme, Developer-ID-Signatur und Notarisierung als Vorschau gekennzeichnet. Nicht zum Abschalten von Gatekeeper auffordern.

`python tools/release-gate.py --evidence <private-abnahme.json> --output <bericht.json>` liefert Exit 2, solange Abnahmebelege fehlen. Die Eingabefelder stehen im Skript; nur bestätigte Belege dürfen `true` sein. Offline-Tests dürfen insbesondere keine interaktive oder echte Kontoabnahme ersetzen.

Für die neue Anbieterstufe mit Testkonten prüfen: richtigen Schlüsseltyp/Region, erste Abfrage, Datenstand und Einheiten, Anmeldung/Abbruch/Abmeldung, getrennte Profile, Schlüssel löschen und Zähler-Konfiguration im verwendeten KI-Client. Eine 403-Portalantwort oder ein erfolgreicher Fake-HTTP-Test ist kein erfolgreicher Browserlogin. Für Anbieter ohne belegten Anschluss muss die Abgrenzung in der Matrix akzeptiert sein.

Quellen: [Anbietermatrix](PROVIDER_MATRIX.md), [Messumfang](PROVIDERS.md), [Datenfluss](PRIVACY.md), [Lokaler Zähler](API_METER.md), [Mac-Prüfstand](MACOS.md), [Komponenten](../THIRD_PARTY_NOTICES.md).
