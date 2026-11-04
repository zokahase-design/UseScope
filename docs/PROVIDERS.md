# Verbindungen und Messumfang

Stand: 04.11.2026. Ein Modellname bezeichnet kein eigenes Abrechnungskonto.
Entscheidend ist der Anbieter, über den deine Anfragen bezahlt werden. Die App
leitet keine Kosten aus Preislisten und keine Tokenzahlen aus Guthabenänderungen ab.

| Adapter | Datenquelle und Berechtigung | Messumfang |
|---|---|---|
| Codex | Offizieller lokaler [App-Server](https://learn.chatgpt.com/docs/app-server) nach CLI-Anmeldung | Abo-Fenster, Reset, Credits und optionaler account/usage/read-Tokenbericht, Zeitraum explizit |
| Claude | Claude-Code-Anmeldung; private OAuth-Kontoendpunkte; optional bewusste lokale JSONL-Zuordnung | Abo-Fenster/Guthaben; lokale CLI-Token nur nach Einrichtung, keine Web-/Gerätegesamtsumme und keine allgemeine öffentliche Konto-API-Zusage |
| Nous | Hermes-Anmeldung; privater Nous-Kontoendpunkt | Nur Nous-Portal-Credits, nicht beliebige Hermes-Provider |
| DeepSeek | API-Key; [GET `/user/balance`](https://api-docs.deepseek.com/api/get-user-balance/) | `total_balance` je CNY/USD; Währungen werden nicht summiert. Keine kontoweite Tokenhistorie oder Kostensumme |
| Kimi International | Internationaler API-Key; [GET `/v1/users/me/balance`](https://platform.kimi.ai/docs/api/balance) | `available_balance` in USD einschließlich Cash und Voucher; kein Kimi-Chat-/Code-Abo |
| OpenRouter | API-Key; [GET `/api/v1/key`](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key) | Schlüsselverbrauch gesamt sowie Tag/Woche/Monat, soweit gemeldet. `limit_remaining` ist ein Schlüsselbudget, kein Wallet. BYOK-Zahlen werden nicht stillschweigend hinzuaddiert |
| OpenAI API | Admin-Key; [Usage](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage/methods/completions) und [Costs](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage/methods/costs) | Laufender UTC-Monat, gesamte Organisation, alle Antwortseiten. Text-Input/Output-Token getrennt; API-Kosten können weitere Produktarten enthalten. Kein Guthaben und kein ChatGPT-Abo |
| xAI API | Management-Key und Team-UUID; [GET Team Prepaid Balance](https://docs.x.ai/developers/rest-api-reference/management/billing) | `total.val` als vorzeichenbehaftete USD-Cents, geteilt durch 100. Anzeige als Teamsaldo, Vorzeichen unverändert. Kein SuperGrok-/X-Abo, keine Tokenhistorie |

API-Konten zeigen den selbst gewählten Kontonamen; eine API-Abrechnung liefert
nicht unbedingt eine E-Mail-Adresse. API-Abfragen sind reine Leseabfragen und
erzeugen keine kostenpflichtigen Modellantworten. Abrechnung kann verzögert sein.
Bei Fehlern bleiben letzte Werte als veraltet gekennzeichnet; 429-Wartezeiten
gelten auch für manuelle Aktualisierung. Leere oder unvollständige Antworten sind
kein Beweis für einen Nullbetrag.

## Ergänzte direkte Adapter in 2.2.0

| Adapter | Offizielle Quelle / Anmeldung | Messumfang und Grenze |
|---|---|---|
| Anthropic API | [Usage/Cost Analytics](https://platform.claude.com/docs/en/manage-claude/usage-cost-analytics-api), Admin-Key | UTC-Monat bis zur letzten vollständigen Stunde, Token inkl. Cache, USD-Cents geteilt durch 100. Provider-Snapshot wird angezeigt; 4–24h Verzögerung möglich. Priority-Vertragsgebühren nicht stillschweigend enthalten |
| Meta Muse Code | [MSP-Protokoll](https://meta-models.github.io/muse-code-sdk/next/guides/msp-wire/), eigene Muse-CLI-Anmeldung | `usage/refresh` experimentell; pro Host begrenzt. Fallback `usage/read` ist nur zuletzt beobachteter Anbieterstand. Gemeinsame Abo-Fenster, keine getrennten Image-/Wonder-Wallets |
| MiniMax M-Plan | [Hersteller-CLI](https://github.com/MiniMax-AI/cli), `mmx auth login` | Explizite Intervall-/Wochen-Restprozente. Uneindeutige historische `usage_count`-Werte werden nicht interpretiert. Status 3 mit Nullkontingent bedeutet kein zugesichertes freies Video |
| MiniMax Global PAYG | [MiniMax Platform](https://platform.minimax.io), `/account/query_balance` | Verfügbarer Betrag. Die Antwort liefert keinen Währungscode: Nutzer übernimmt USD/CNY ausdrücklich aus seinem Portal; Anzeige mit `*`. M-Plan und Hailuo separat |
| Google Gemini API / Cloud | [Cloud Monitoring](https://cloud.google.com/monitoring/api/metrics_gcp_d_h), `gcloud auth login`, Projekt-IAM | Projekt-Anfragen und gemeldete Output-Token; keine erfundenen Input-Token und kein Gemini-App-Abo. Kosten nur optional aus konkret angegebener bestehender [Billing-Exporttabelle](https://cloud.google.com/billing/docs/how-to/export-data-bigquery). BigQuery-Abfragen können kostenpflichtig sein |
| Qwen/Wan Cloud | [BSS Account Balance](https://www.alibabacloud.com/help/en/user-center/developer-reference/api-bssopenapi-2017-12-14-queryaccountbalance), QueryBillOverview; RAM-Leserechte | Gemeinsames Cashguthaben ohne Kreditrahmen; laufende Monatskosten für ganzes Cloudkonto oder expliziten Produktcode. Qwen/Wan können dasselbe Wallet zeigen; niemals doppelt addieren |
| Alibaba Coding-/Tokenplan | [Bailian Usage CLI](https://help.aliyun.com/en/model-studio/cli/usage-quota), Console-Login, eigene Site/Region | Coding-Fenster bzw. tatsächlich gemeldete dynamische Tokenfenster; keine Cloud-Geldsaldo-Umrechnung. Internationale Abdeckung hängt von Hersteller-CLI und Konto ab |
| Arcee API | [Usage summary](https://docs.arcee.ai/api-reference/usage), Bearer-Key | Gemeldete Input-/Output-/Gesamt-Token und Anfragen im UTC-Monat; keine Kostensumme aus Tokenpreisen |
| StepFun International | [Accounts API](https://platform.stepfun.ai/docs/en/api-reference/accounts/get), API-Key | Aktueller Saldo, USD gemäß internationaler Preisliste (`*`, kein Währungsfeld). Lifetime-Einzahlungen und Voucher werden nicht addiert; Step Plan separat |
| Mistral Enterprise Admin | [Admin Usage](https://docs.mistral.ai/admin/admin-api/usage-metrics), dedizierter `x-api-key` | Gemeldete API-Aufrufsumme im UTC-Monat. Enterprise-Zugang erforderlich; heterogene Billing-Metriken sind keine allgemeine Text-Token-/Kostensumme. Kein Le-Chat-Abo |
| DigitalOcean Cloud Billing | [Customer balance](https://docs.digitalocean.com/reference/api/reference/billing/), PAT mit `billing:read` | Gesamtes Cloudkonto: Monatssaldo und Monatsverbrauch mit gemeldetem Zeitstand. Vorzeichen unverändert, USD gemäß Anbieterabrechnung (`*`). Kein reiner KI-Verbrauch; Inference-Key separat |

## Optionaler lokaler API-Zähler

46 der 53 Anbieterfamilien besitzen einen Preset für unterstützte API-Protokolle, 39 mit fester öffentlicher Basisadresse und 7 mit eigener Ressourcen-/Konto-Adresse. Zusätzlich bestehen direkte Adapter und separate Produkte wie Nous, Arcee und OpenRouter. Diese Zahlen sind keine Zahl getesteter Livekonten. [Alle Anbieter und Quellen](PROVIDER_MATRIX.md), [Einrichtung, Speicherung und Grenzen](API_METER.md).

Ein OpenAI-kompatibler Inferenz-Endpunkt ist keine kontoweite Billing-API. Der Zähler erfasst nur geroutete Anfragen. Fehlende Token oder Geldfelder bleiben unbekannt. Bei Medien-/Videodiensten ist eine Anfrage nicht automatisch eine Tokenmenge; asynchrone Jobs und Training sind nicht pauschal abgedeckt.

Grok/SuperGrok bleibt als separates App-Portal verfügbar. InclusionAI-Modellnutzung wird beim tatsächlichen Host gemessen. Für Thinking Machines/Tinker, Makora, Modal, Wafer und Modular existiert in diesem Prüfstand noch kein verifizierter automatischer Adapter. Sie werden als Info/Portal gekennzeichnet. Das ist eine offene Abdeckungslücke und kein Beleg technischer Unmöglichkeit.

## Anmeldeprüfung und ihre Grenzen

- Die Anbieterauswahl, getrennte Profile, API-Key-Eingaben, feste HTTPS-Ziele,
  Fehlermeldungen, Pagination, Kontowechsel und Löschung werden offline mit
  erfundenen Daten geprüft. Windows prüft zusätzlich DPAPI-Verschlüsselung,
  kontogebundene Entschlüsselung und das Entfernen des lokalen Schlüssels.
- Die Portal-URLs wurden ohne Anmeldung per HTTP geprüft. Bot-Schutz und
  Länderweiterleitungen können automatisierte Anfragen blockieren. Ein HTTP 403
  beweist weder einen kaputten Link noch eine funktionierende Anmeldung.
- Es wurden keine fremden Testkonten angelegt, keine Passwörter eingegeben und
  keine echten API-Schlüssel für sämtliche Anbieter beschafft. Ein vollständiger
  Erstanmeldetest bei jedem Anbieter ist damit nicht abgeschlossen.
- Der Mac-Build und virtuelle Darstellungen ersetzen keine native Abnahme von
  Browserrückkehr, Menüleiste und Schlüsselbund auf beiden Prozessorarchitekturen.

Ein neuer Adapter benötigt dokumentierte Messwerte, Einheiten, Rechte und
Kontozuordnung. Eine OpenAI-kompatible Text-API allein garantiert keine kompatible
Abrechnungs-API.
