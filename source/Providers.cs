using System.Diagnostics;

namespace UseScope.Desktop;

internal enum ConnectionKind { Native, ApiKey, Portal, LocalMeter }
internal sealed record Provider(string Id, string Name, ConnectionKind Connection, string Portal, string Help, string Badge, string? Icon = null);
internal static class Providers
{
    internal static readonly Provider[] All = CreateCore().Concat(ProviderCatalog.Additional).ToArray();
    private static Provider[] CreateCore() => [
        new("codex", "OpenAI Codex", ConnectionKind.Native, "https://chatgpt.com/codex", "Anmeldung über die offizielle Codex-CLI; diese öffnet den Browser. ChatGPT/Codex-Abo, getrennt von API-Abrechnung.", "O"),
        new("claude", "Anthropic Claude", ConnectionKind.Native, "https://claude.ai/settings/usage", "Anmeldung über Claude Code im Browser. CLI erforderlich; private Kontoabfrage, keine öffentliche API-Garantie.", "C"),
        new("nous", "Hermes / Nous", ConnectionKind.Native, "https://portal.nousresearch.com", "Anmeldung über die Hermes-CLI im Browser. Guthaben im Nous Portal, keine Messung aller Hermes-Modellanbieter.", "N"),
        new("deepseek", "DeepSeek API", ConnectionKind.ApiKey, "https://platform.deepseek.com", "API-Schlüssel aus dem offiziellen Portal. Guthaben in der gemeldeten Währung; keine kontoweite Tokenhistorie.", "D", "deepseek.png"),
        new("moonshot", "Kimi / Moonshot API (International)", ConnectionKind.ApiKey, "https://platform.moonshot.ai", "Internationaler Moonshot-API-Schlüssel. API-Guthaben in USD; ein Kimi-Chat-Abo ist davon getrennt.", "K", "kimi.png"),
        new("openrouter", "OpenRouter API", ConnectionKind.ApiKey, "https://openrouter.ai/settings/keys", "API-Schlüssel: dessen USD-Verbrauch und Ausgabenlimit. Kein Kontoguthaben und keine Tokenhistorie. Erfasst auch andere Modelle, wenn sie über diesen Schlüssel laufen.", "R", "openrouter.png"),
        new("openai-api", "OpenAI API (Organisation)", ConnectionKind.ApiKey, "https://platform.openai.com/settings/organization/admin-keys", "Admin-API-Schlüssel erforderlich. Organisationskosten und Text-Input/Output-Token des laufenden UTC-Monats. Kein ChatGPT-Abo und kein Guthaben.", "O"),
        new("xai", "xAI / Grok API (Team)", ConnectionKind.ApiKey, "https://console.x.ai", "Management-API-Schlüssel und Team-ID erforderlich. Vorzeichenbehafteter Teamsaldo in USD; kein Grok-Abo und keine Tokenhistorie.", "X", "xai.png"),
        new("grok", "Grok / SuperGrok · Portal", ConnectionKind.Portal, "https://grok.com", "Grok-App-Abo im offiziellen Portal verwalten; keine automatische Messung in UseScope. Getrennt vom xAI-API-Konto.", "X", "xai.png"),
        new("gemini", "Google Gemini API / Cloud", ConnectionKind.Native, "https://aistudio.google.com", "Google-Cloud-CLI (gcloud), eigenes Browserlogin und Projekt-ID. Projekt-Anfragen und Output-Token; optional bestehender Billing-Export für Projektkosten. Kein Gemini-App-Abo.", "G", "gemini.png"),
        new("qwen", "Qwen / Alibaba Cloud", ConnectionKind.ApiKey, "https://modelstudio.console.alibabacloud.com", "RAM-Lesezugang für Cloud-Abrechnung (AccessKey-ID und Secret). Gemeinsames Cloud-Cashguthaben und Monatskosten; optional Produktcode. Kein Chat-Abo und keine aus Geld abgeleiteten Token.", "Q", "qwen.png"),
        new("wan", "Wan / Alibaba Cloud", ConnectionKind.ApiKey, "https://modelstudio.console.alibabacloud.com", "RAM-Lesezugang für die Cloud-Abrechnung. Wallet kann mit Qwen identisch sein: nicht doppelt addieren. Monatskosten für Cloudkonto oder gewählten Produktcode, keine erfundene Video-Anzahl.", "W", "wan.png"),
        new("minimax", "MiniMax M-Plan", ConnectionKind.Native, "https://platform.minimax.io", "Offizielle MiniMax-CLI (mmx), eigenes Login. Intervall-/Wochenverbrauch der tatsächlich gemeldeten Plangruppen. M-Plan, PAYG und Hailuo sind getrennte Produkte.", "M", "minimax.png"),
        new("meta", "Meta Muse Code · Image / Wonder", ConnectionKind.Native, "https://dev.meta.ai", "Muse-Code-CLI separat installieren. Im Terminal Browserlogin abschließen und Muse danach mit /exit beenden. Abo-Fenster über Herstellerprotokoll; Datenstand beachten. Kein Model-API-Wallet.", "M", "meta.png"),
        new("anthropic-api", "Anthropic API (Organisation)", ConnectionKind.ApiKey, "https://platform.claude.com", "Admin-API-Schlüssel für aktuelle Usage/Cost Analytics. Organisationskosten und Token einschließlich Cache, laufender UTC-Monat; bis zu 24h Datenverzug. Getrennt vom Claude-Abo.", "C"),
        new("minimax-api", "MiniMax API · H3 / PAYG (Global)", ConnectionKind.ApiKey, "https://platform.minimax.io", "Globaler PAYG-Key (kein M-Plan-Key). Verfügbarer Betrag; Kontowährung selbst aus dem Portal übernehmen, da die API sie nicht liefert. Kein Hailuo-App-Abo.", "M", "minimax.png"),
        new("alibaba-coding", "Qwen / Alibaba Coding Plan", ConnectionKind.Native, "https://modelstudio.console.alibabacloud.com", "Offizielle Bailian-CLI (bl ab 2.7.2), isoliertes Console-Browserlogin. 5h-/Wochen-/Monatsplan. Site-Abdeckung der Hersteller-CLI prüfen.", "Q", "qwen.png"),
        new("alibaba-token", "Qwen / Alibaba Token Plan", ConnectionKind.Native, "https://modelstudio.console.alibabacloud.com", "Offizielle Bailian-CLI (bl ab 2.7.2), isoliertes Console-Browserlogin. Tatsächlich gemeldete Tokenplan-Fenster; kein Cloud-Geldsaldo.", "Q", "qwen.png")
    ];
    internal static Provider Get(string id) => All.Single(provider => provider.Id == id);
    internal static bool Known(string id) => All.Any(provider => provider.Id == id);
    internal static string? Icon(string id, bool dark)
    {
        if (id is "codex" or "openai-api") return dark ? "OpenAI-white.png" : "OpenAI-black.png";
        if (id is "claude" or "anthropic-api") return "Claude.png";
        var provider = Get(id); var row = ProviderCatalog.Rows.FirstOrDefault(item => item.Id == (id == "digitalocean-billing" ? "digitalocean" : id));
        return (dark ? row?.IconDark : row?.IconLight) ?? provider.Icon ?? row?.Icon;
    }
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
