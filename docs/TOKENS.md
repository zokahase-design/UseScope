# Tokenmetadaten und Messgrenzen

Seit UseScope 2.6.2 zeigt die Oberfläche Limits, Resetzeiten, gemeldetes
Guthaben und die übrigen verfügbaren Verbrauchsdaten ohne Tokenzahlen an.
Vorhandene Kontoeinstellungen bleiben beim Update erhalten.

## Datenquellen

| Verbindung | Intern verarbeitete Tokenmetadaten |
|---|---|
| Codex-Abo | Gemeldete Tageswerte oder Gesamtsumme aus dem Kontobericht derselben CLI-Verbindung, höchstens alle 15 Minuten |
| Claude-Abo mit bestehender lokaler Zuordnung | Tokenfelder aus früher ausdrücklich zugeordneten Claude-Code-Sitzungsdateien |
| OpenAI- und Anthropic-Organisations-APIs | Gemeldete API-Nutzung im laufenden UTC-Monat, mit je nach Produkt unterschiedlicher Abdeckung |
| Arcee API und Google Cloud | Arcee-Kontobericht bzw. Gemini-Output im jeweiligen Google-Cloud-Projekt |
| Optionaler lokaler API-Zähler | Gemeldete Token zu durchgeleiteten Anfragen im lokalen Nutzungsjournal |

Fehlende Angaben werden nicht aus Limits, Credits oder Preisen geschätzt.
Anbieterberichte und lokale Journale können zeitlich verzögert oder
unvollständig sein. Der lokale API-Zähler erfasst nur durch UseScope
geleitete Anfragen. Details stehen in [API-Zähler](API_METER.md) und
[Datenschutz und Datenfluss](PRIVACY.md).

Eine bestehende Claude-Ordnerzuordnung gilt nur bei bestätigter
Konto-/Organisationskennung. Die Verarbeitung ist mengen- und zeitbegrenzt.
Sie umfasst keine Claude-Webnutzung oder Sitzungen anderer Geräte und kopiert
keine Prompts oder Antworttexte. Für neue Konten bietet die Oberfläche keine
Ordnerzuordnung an. Eine frühere Zuordnung lässt sich im Kontomenü
über „Lokalen Claude-Verlauf trennen“ aufheben.
