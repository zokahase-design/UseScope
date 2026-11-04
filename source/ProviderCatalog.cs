using System.Text.Json;

namespace UseScope.Desktop;

internal sealed record MeterEndpoint(string Base, string Auth = "bearer");
internal static class ProviderCatalog
{
    internal sealed record Row(string Id, string Name, string? Portal, string? Documentation, bool Meter, string Base, string Auth, string? Protocol, bool RequiresEndpoint, string? Icon, string? IconDark, string? IconLight);
    private sealed record Document(int Schema, Row[] Providers);
    internal static readonly Row[] Rows = Load();
    private static Row[] Load()
    {
        using var stream = typeof(ProviderCatalog).Assembly.GetManifestResourceStream("UseScope.ProviderCatalog") ?? throw new InvalidDataException("Anbieterkatalog fehlt.");
        var document = JsonSerializer.Deserialize<Document>(stream) ?? throw new InvalidDataException("Anbieterkatalog ungültig.");
        if (document.Schema != 1 || document.Providers.Select(item => item.Id).Distinct().Count() != document.Providers.Length) throw new InvalidDataException("Anbieterkatalog inkonsistent.");
        return document.Providers;
    }
    internal static readonly Provider[] Additional = [
        new("arcee", "Arcee API", ConnectionKind.ApiKey, "https://app.arcee.ai", "API-Schlüssel für den Monatsbericht: gemeldete Token und Anfragen, keine Kostenberechnung.", "A"),
        new("stepfun", "StepFun API (International)", ConnectionKind.ApiKey, "https://platform.stepfun.ai", "Internationaler API-Schlüssel. Aktueller Saldo in USD gemäß internationaler Preisliste; Step Plan separat.", "S"),
        new("mistral-admin", "Mistral API (Enterprise-Administration)", ConnectionKind.ApiKey, "https://admin.mistral.ai", "Dedizierter Enterprise-Admin-Key. Monats-Anfragen aus dem Admin-Bericht; Standard-API-Key hierfür nicht ausreichend.", "M"),
        new("digitalocean-billing", "DigitalOcean · Cloud-Abrechnung", ConnectionKind.ApiKey, "https://cloud.digitalocean.com/account/api/tokens", "Personal Access Token mit billing:read. Monatlicher Cloud-Gesamtverbrauch und vorzeichenbehafteter Saldo, kein reiner KI-Verbrauch. Inference-Key separat für lokalen API-Zähler.", "D"),
        new("custom-api", "Eigener API-Endpunkt · lokaler Zähler", ConnectionKind.LocalMeter, "https://github.com/zokahase-design/UseScope", MeterSetup.Help, "↗"),
        .. Rows.Where(item => !new[] { "gemini", "anthropic-api", "openai-api", "moonshot", "meta", "xai", "qwen", "deepseek", "minimax-api", "stepfun", "mistral-admin" }.Contains(item.Id)).Select(item =>
            new Provider(item.Id, item.Name + (item.Meter ? " · lokaler API-Zähler" : " · Info / Portal"), item.Meter ? ConnectionKind.LocalMeter : ConnectionKind.Portal,
                item.Portal ?? item.Documentation ?? "https://github.com/zokahase-design/UseScope/blob/main/docs/PROVIDERS.md",
                item.Meter ? "Lokaler API-Zähler: nur durchgeleitete Anfragen und gemeldete Token/Kosten. Kein Konto- oder Abo-Restlimit. Inference-Key erforderlich; UseScope muss laufen." + (item.RequiresEndpoint ? " Eigene Ressourcen-/Workspace-Adresse erforderlich." : "") + (item.Id == "coreweave" ? " Forge/W&B Inference, nicht gesamte Cloud. Team/Projekt im Client senden." : "")
                    : item.Id == "inclusionai" ? "Modellhersteller: die API wird vom jeweiligen Hosting-Anbieter abgerechnet. Bitte dessen Konto verbinden. Kein eigener Kontoabruf in UseScope."
                    : "Noch kein verifizierter automatischer Anschluss. Portal/Information öffnen; keine Nutzungsmessung. Für einen öffentlich dokumentierten kompatiblen API-Endpunkt kann ein eigener lokaler Zähler eingerichtet werden.", item.Name[..1], item.Icon))
    ];
    internal static MeterEndpoint Endpoint(string vendor) => vendor switch {
        "openai-api" => new("https://api.openai.com/v1/"),
        "anthropic-api" => new("https://api.anthropic.com/v1/", "x-api-key"),
        "deepseek" => new("https://api.deepseek.com/v1/"), "moonshot" => new("https://api.moonshot.ai/v1/"),
        "openrouter" => new("https://openrouter.ai/api/v1/"), "xai" => new("https://api.x.ai/v1/"),
        "gemini" => new("https://generativelanguage.googleapis.com/v1beta/", "x-goog-api-key"),
        "qwen" or "wan" => new("https://dashscope-intl.aliyuncs.com/compatible-mode/v1/"),
        "minimax-api" => new("https://api.minimax.io/v1/"), "stepfun" => new("https://api.stepfun.ai/v1/"),
        "arcee" => new("https://api.arcee.ai/api/v1/"), "mistral-admin" => new("https://api.mistral.ai/v1/"),
        _ => Rows.FirstOrDefault(item => item.Id == vendor) is Row row ? new(row.Base, row.Auth) : new("")
    };
    internal static bool AllowsMeter(Provider provider) => provider.Connection == ConnectionKind.LocalMeter || provider.Connection != ConnectionKind.Portal && provider.Id is not ("codex" or "claude" or "nous" or "meta" or "minimax" or "alibaba-coding" or "alibaba-token" or "digitalocean-billing");
}
