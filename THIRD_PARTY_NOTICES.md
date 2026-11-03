# Komponenten und Anbieterzeichen

## .NET

Der Windows-Download enthält Microsoft.NETCore.App.Runtime.win-x64 und
Microsoft.WindowsDesktop.App.Runtime.win-x64. Diese Komponenten werden unter
ihren eigenen Bedingungen mitgeliefert. Der Paketbau übernimmt die Lizenztexte
aus den tatsächlich verwendeten NuGet-Paketen in `docs/licenses/` des Downloads.
Die zugehörigen Drittanbieterhinweise bleiben ebenfalls enthalten.

## Mac-Oberfläche

Die macOS-Version verwendet zusätzlich Avalonia, MicroCom.Runtime, SkiaSharp und
HarfBuzzSharp samt nativen Bestandteilen. Deren Lizenztexte stehen unter
`docs/licenses/` im Repository und werden zusammen mit den aus den verwendeten
NuGet-Paketen ermittelten Hinweisen in das Mac-ZIP aufgenommen. Das Inventar im
Paket nennt die konkreten Versionen. Diese Komponenten behalten eigene
Copyrightvermerke und Lizenzbedingungen.

## Anbieterzeichen

OpenAI, Codex, Anthropic, Claude, Hermes und Nous Portal sind Namen ihrer jeweiligen
Rechteinhaber. Dies gilt ebenso für DeepSeek, Kimi/Moonshot, OpenRouter, xAI/Grok, Google/Gemini, Alibaba/Qwen/Wan, MiniMax und Meta/Muse. [Quellen der zusätzlichen Zeichen](docs/BRAND_ASSETS.md). Anbieterlogos dienen der Zuordnung von Konten. Diese Zeichen sind
nicht durch die MIT-Lizenz der Anwendung zur freien Markennutzung freigegeben.

UseScope ist nicht mit den Anbietern verbunden und wird von diesen weder
unterstützt noch gesponsert. Für OpenAI gelten gesonderte
[Markenbedingungen](https://openai.com/brand/).

## Anbieterprogramme

Die Codex-, Claude-Code- und Hermes-CLIs werden nicht mitgeliefert. Installation,
Anmeldung, Lizenzen und Nutzungsbedingungen dieser Programme bleiben beim
jeweiligen Anbieter. Die MIT-Lizenz ist keine Freigabe zur Verwendung privater
Anbieterschnittstellen.
