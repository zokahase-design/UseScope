using System.Diagnostics;

namespace UseScope.Desktop;

internal enum ConnectionKind { Native, ApiKey, Portal }
internal sealed record Provider(string Id, string Name, ConnectionKind Connection, string Portal, string Help, string Badge, string? Icon = null);
internal static class Providers
{
    internal static readonly Provider[] All = [
        new("codex", "OpenAI Codex", ConnectionKind.Native, "https://chatgpt.com/codex", "Anmeldung über die offizielle Codex-CLI; diese öffnet den Browser. ChatGPT/Codex-Abo, getrennt von API-Abrechnung.", "O"),
        new("claude", "Anthropic Claude", ConnectionKind.Native, "https://claude.ai/settings/usage", "Anmeldung über Claude Code im Browser. CLI erforderlich; private Kontoabfrage, keine öffentliche API-Garantie.", "C"),
        new("nous", "Hermes / Nous", ConnectionKind.Native, "https://portal.nousresearch.com", "Anmeldung über die Hermes-CLI im Browser. Guthaben im Nous Portal, keine Messung aller Hermes-Modellanbieter.", "N"),
        new("deepseek", "DeepSeek API", ConnectionKind.ApiKey, "https://platform.deepseek.com", "API-Schlüssel aus dem offiziellen Portal. Guthaben in der gemeldeten Währung; keine kontoweite Tokenhistorie.", "D", "deepseek.png"),
        new("moonshot", "Kimi / Moonshot API (International)", ConnectionKind.ApiKey, "https://platform.moonshot.ai", "Internationaler Moonshot-API-Schlüssel. API-Guthaben in USD; ein Kimi-Chat-Abo ist davon getrennt.", "K", "kimi.png"),
        new("openrouter", "OpenRouter API", ConnectionKind.ApiKey, "https://openrouter.ai/settings/keys", "API-Schlüssel: dessen USD-Verbrauch und Ausgabenlimit. Kein Kontoguthaben und keine Tokenhistorie. Erfasst auch andere Modelle, wenn sie über diesen Schlüssel laufen.", "R", "openrouter.png"),
        new("openai-api", "OpenAI API (Organisation)", ConnectionKind.ApiKey, "https://platform.openai.com/settings/organization/admin-keys", "Admin-API-Schlüssel erforderlich. Organisationskosten und Text-Input/Output-Token des laufenden UTC-Monats. Kein ChatGPT-Abo und kein Guthaben.", "O"),
        new("xai", "xAI / Grok API (Team)", ConnectionKind.ApiKey, "https://console.x.ai", "Management-API-Schlüssel und Team-ID erforderlich. Vorzeichenbehafteter Teamsaldo in USD; kein Grok-Abo und keine Tokenhistorie.", "X", "xai.png"),
        new("grok", "Grok / SuperGrok · Portal", ConnectionKind.Portal, "https://grok.com", "Grok-App-Abo im offiziellen Portal verwalten; keine automatische Messung in UseScope. Getrennt vom xAI-API-Konto.", "X", "xai.png"),
        new("gemini", "Google Gemini · Portal", ConnectionKind.Portal, "https://aistudio.google.com", "AI Studio: API-Verbrauch und Abrechnung prüfen. Google-AI-Abo und API-Projekt sind getrennt. Keine automatische Messung in UseScope.", "G", "gemini.png"),
        new("qwen", "Qwen / Alibaba · Portal", ConnectionKind.Portal, "https://modelstudio.console.alibabacloud.com", "Model Studio: Qwen-API-Verbrauch im zugehörigen Alibaba-Konto. Keine automatische Messung in UseScope.", "Q", "qwen.png"),
        new("wan", "Wan / Alibaba · Portal", ConnectionKind.Portal, "https://modelstudio.console.alibabacloud.com", "Wan-API über Alibaba Model Studio: Videoabrechnung im Alibaba-Konto. Keine automatische Messung; nicht als Qwen-Kosten doppelt zählen.", "W", "wan.png"),
        new("minimax", "MiniMax / Hailuo · Portal", ConnectionKind.Portal, "https://platform.minimax.io", "Offizielles MiniMax-API-Portal. API, Tokenplan und Hailuo-Abo getrennt prüfen. Keine automatische Messung in UseScope.", "M", "minimax.png"),
        new("meta", "Meta Muse Image / Wonder · Portal", ConnectionKind.Portal, "https://dev.meta.ai", "Meta Model API: Muse-Modelle im Anbieterportal verwalten. Keine automatische Messung in UseScope.", "M", "meta.png"),
        new("anthropic-api", "Anthropic API · Portal", ConnectionKind.Portal, "https://platform.claude.com", "API-Abrechnung und Claude-Abo sind getrennt. Admin-Reporting ist hier noch nicht angebunden; keine automatische Messung.", "C")
    ];
    internal static Provider Get(string id) => All.Single(provider => provider.Id == id);
    internal static bool Known(string id) => All.Any(provider => provider.Id == id);
    internal static string? Icon(string id, bool dark) => id is "codex" or "openai-api" ? dark ? "OpenAI-white.png" : "OpenAI-black.png"
        : id is "claude" or "anthropic-api" ? "Claude.png" : Get(id).Icon;
    internal static void OpenPortal(string id)
    {
        var url = new Uri(Get(id).Portal);
        if (url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo)) throw new InvalidOperationException();
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
    }
    internal static string ValidateKey(string value)
    {
        var key = value.Trim();
        if (key.Length is < 8 or > 4096 || key.Any(character => character <= ' ' || character > '~'))
            throw new ArgumentException("Bitte einen gültigen API-Schlüssel ohne Leerzeichen eingeben.");
        return key;
    }
}
