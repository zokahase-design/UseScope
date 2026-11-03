# Verbindungen und Messumfang

Stand: 03.11.2026. Ein Modellname bezeichnet kein eigenes Abrechnungskonto.
Entscheidend ist der Anbieter, über den deine Anfragen bezahlt werden. Die App
leitet keine Kosten aus Preislisten und keine Tokenzahlen aus Guthabenänderungen ab.

| Adapter | Datenquelle und Berechtigung | Messumfang |
|---|---|---|
| Codex | Offizieller lokaler [App-Server](https://developers.openai.com/codex/app-server/) nach CLI-Anmeldung | Vom Anbieter gemeldete Abo-Fenster, Reset, Credits |
| Claude | Claude-Code-Anmeldung; private OAuth-Kontoendpunkte | Gemeldete Abo-Fenster und Prepaid-Guthaben; keine allgemeine öffentliche API-Zusage |
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

## Portale in dieser Version

Diese Einträge zeigen Anbieterlogo, Bezeichnung und einen klaren Portalstatus.
Sie öffnen auf Nutzeraktion die offizielle Website. Eine Anmeldung dort wird
nicht als UseScope-Verbindung ausgegeben und liefert der App keine Cookies oder Schlüssel.

| Eintrag | Offizieller Zugang | Automatisierung: Abgrenzung |
|---|---|---|
| Grok / SuperGrok | [Grok](https://grok.com) | App-Abo getrennt von xAI API |
| Gemini | [Google AI Studio](https://aistudio.google.com) | API-Projekt und Google-AI-Abo getrennt. Google dokumentiert [Billing im Dashboard](https://ai.google.dev/gemini-api/docs/billing); Cloud Monitoring benötigt gesonderte OAuth-/IAM-Rechte |
| Qwen | [Alibaba Model Studio](https://modelstudio.console.alibabacloud.com) | Cloud-Abrechnung und Coding-/Tokenpläne besitzen eigene Berechtigungen. Eine Model-Studio-API-Key-Verbindung allein ist keine Cloud-Billing-Verbindung |
| Wan | [Alibaba Model Studio](https://modelstudio.console.alibabacloud.com) | Videokosten im verwendeten API-/Cloud-Konto. Nicht denselben Cloud-Gesamtbetrag zusätzlich als Qwen-Guthaben zählen |
| MiniMax / Hailuo | [MiniMax API Platform](https://platform.minimax.io) | PAYG/H3, Hailuo und M-Plan unterscheiden. Ein [dokumentierter M-Plan-Abruf](https://www.minimax.io/m-plan) ist kein belegtes H3-Guthaben; in dieser Version noch kein M-Plan-Adapter |
| Meta Muse Image / Wonder | [Meta Developers](https://dev.meta.ai) | Muse-Code-Abo und Model-API-Abrechnung unterscheiden. Meta dokumentiert [`muse usage`](https://dev.meta.ai/docs/muse-code/changelog); dieser CLI-Adapter ist noch nicht eingebaut |
| Anthropic API | [Claude API Platform](https://platform.claude.com) | API-Organisation getrennt vom Claude-Abo. [Admin-Analytics](https://platform.claude.com/docs/en/manage-claude/usage-cost-analytics-api) benötigen eigene Rechte und einen eigenen Adapter |

Alibaba bietet ebenfalls [offizielle CLI-Abfragen für Coding- und Tokenpläne](https://docs.modelstudio.console.alibabacloud.com/en/model-studio/cli/usage-quota)
und eine gesonderte [Cloud-Balance-API](https://www.alibabacloud.com/help/en/user-center/developer-reference/api-bssopenapi-2017-12-14-queryaccountbalance).
Dass diese Adapter hier noch fehlen, bedeutet nicht, dass der Anbieter grundsätzlich
keine Abfragemöglichkeit besitzt. Kosten aus Bild- und Videogenerierung lassen sich
auch nicht sinnvoll pauschal als Text-Token behandeln.

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
