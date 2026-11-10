# UseScope starten

1. Dieses ZIP vollständig entpacken.
2. Im entpackten Ordner `UseScope starten.exe` öffnen.
3. Über das Pluszeichen oben rechts im Widget oder das Tray-Menü ein Konto hinzufügen. Je nach Auswahl in der Anbieter-CLI anmelden, einen API-Schlüssel selbst hinterlegen oder das Anbieterportal öffnen.

Der Starter findet `runtime\UseScope.exe` relativ zu seinem eigenen Ordner.
Beide müssen in dieser Anordnung zusammenbleiben. Die Anwendung und ihre .NET-10-
Laufzeit liegen gemeinsam unter `runtime`. Bei Problemen mit dem kleinen Starter
kann `runtime\UseScope.exe` auch direkt geöffnet werden.

Für Codex, Claude und Nous muss die jeweilige Anbieter-CLI separat installiert sein. UseScope enthält keine
Codex-, Claude-Code- oder Hermes-Installation und keine vorbereiteten Konten.
Weitere native Verbindungen benötigen entsprechend Muse, mmx, gcloud oder Bailian. Der Kontodialog nennt den Weg.

Für den optionalen lokalen API-Zähler im KI-Client die angezeigte lokale API-Adresse und den lokalen Schlüssel eintragen; den eigentlichen Inference-Key speichert UseScope geschützt. UseScope muss während der API-Nutzung laufen. Nur dadurch geleitete Nutzung wird gezählt, keine historische Kontosumme. Siehe API_METER.md und PROVIDER_MATRIX.md.

Linksklick auf das Tray-Symbol oder `Strg+Alt+U` zeigt das Widget an und holt es bei Bedarf nach vorne. Zum gezielten Ausblenden dient der Menüpunkt **Widget ausblenden**.
Über **Widget nach vorne holen** im Tray-Menü lässt sich auch ein verdecktes Widget bei offenem Schloss direkt anzeigen.
Rechtsklick öffnet Konten, Aktualisieren, Darstellung, Windows-Start und Beenden.
Das Menü gruppiert Schnellaktionen, Konto hinzufügen/Konten sowie Darstellung
und Programmende. Im Konto-Untermenü folgen auf Werte und Aktualisieren die
Anzeigeoptionen, Verbindungsaktionen und die Kontoverwaltung.
Am Schloss wird der Vordergrundmodus umgeschaltet. Das Listensymbol aktiviert
die Kontosortierung. Das Widget lässt sich an der Überschrift verschieben.

Unter Darstellung → Limitbalken zwischen verbrauchtem Kontingent (Balken füllen
sich von 0 auf 100 %) und verfügbarem Kontingent (Balken leeren sich von 100 auf
0 %) wählen. Die Prozentzahl wechselt mit, die Auswahl bleibt gespeichert.

Gemeldete Nullwerte bleiben sichtbar, z. B. `0 Credits` oder `0 %`. Abgelaufene
Resetzeiten erscheinen als `0h 0min`. Meldet der Anbieter kein Guthaben oder
keinen Resetzeitpunkt, steht dort `Nicht gemeldet` bzw. `Unbekannt`; diese Angaben
sind keine gemessenen Nullwerte. Vor der ersten Abfrage steht `Wird abgefragt …`.

Die Limitbalken nutzen die Breite der Kontozeile. „Werte und Verbindung erklären…“
im Kontomenü (Mac: „Infos“) zeigt weitere Angaben zu den gemeldeten Werten.

UseScope fragt native Konten automatisch frühestens alle fünf Minuten und
API-Abrechnungen frühestens alle zehn Minuten ab. Konten desselben Anbieters
werden nacheinander mit Abstand abgefragt; es laufen insgesamt höchstens zwei
Anbieterabfragen gleichzeitig. Manuelles Aktualisieren hat einen Mindestabstand
von einer Minute und überspringt keine Anbieterpause. Nach Fehlern werden die
Pausen länger; das Kontomenü erklärt den nächsten Versuch. Letzte bekannte Werte
bleiben bei vorübergehenden Verbindungsfehlern als veraltet gekennzeichnet.
Bei ausgeblendetem Widget pausieren die automatischen Anbieterabfragen; beim
Einblenden werden fällige Abfragen nachgeholt. Der aktivierte lokale API-Zähler
kann weiterhin Anfragen durchleiten und zählen. Manuelle Aktualisierung bleibt möglich.

Bei `Anmeldung erforderlich` das betroffene Konto über `Anmelden…` selbst neu
anmelden. Browser oder Desktop-App müssen dafür nicht allein wegen der
zusätzlichen UseScope-Abfrage geschlossen werden. Fremde Browser-Sitzungen
werden nicht übernommen.

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
