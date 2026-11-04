using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UseScope.Desktop;

internal sealed record ConnectionField(string Id, string Label, bool Secret = true, string Default = "", bool Required = true);
internal static class ConnectionSetup
{
    internal static IReadOnlyList<ConnectionField> Fields(string vendor) => vendor switch {
        "xai" => [new("key", "Management-API-Schlüssel"), new("team", "Team-ID (UUID)", false)],
        "qwen" or "wan" or "alibaba-cloud" => [new("key", "RAM AccessKey-ID", false), new("secret", "RAM AccessKey-Secret"),
            new("site", "Abrechnung: global oder cn", false, "global"), new("product", "Produktcode optional (leer = gesamtes Cloudkonto)", false, "", false)],
        "minimax-api" => [new("key", "Globaler PAYG-API-Schlüssel (kein M-Plan-Key)"), new("currency", "Kontowährung laut deinem Portal: USD oder CNY", false)],
        "digitalocean-billing" => [new("key", "DigitalOcean Personal Access Token (billing:read)")],
        "mistral-admin" => [new("key", "Enterprise Admin-API-Key (kein Standard-Key)")],
        _ => [new("key", vendor == "anthropic-api" ? "Admin-API-Schlüssel" : "API-Schlüssel")]
    };
    internal static string Encode(string vendor, IReadOnlyDictionary<string, string> values)
    {
        string Value(string name) => values.GetValueOrDefault(name)?.Trim() ?? "";
        if (vendor == "minimax-api")
        {
            var key = Providers.ValidateKey(Value("key")); var currency = Value("currency").ToUpperInvariant();
            if (currency is not ("USD" or "CNY")) throw new ArgumentException("Bitte die Kontowährung ausdrücklich aus dem MiniMax-Portal übernehmen (USD oder CNY).");
            return new JsonObject { ["key"] = key, ["currency"] = currency }.ToJsonString();
        }
        if (vendor is "qwen" or "wan" or "alibaba-cloud")
        {
            var id = Providers.ValidateKey(Value("key")); var secret = Providers.ValidateKey(Value("secret")); var site = Value("site"); var product = Value("product");
            if (site is not ("global" or "cn")) throw new ArgumentException("Abrechnungsregion muss global oder cn sein.");
            if (!Regex.IsMatch(product, "^[a-zA-Z0-9_-]{0,80}$")) throw new ArgumentException("Ungültiger Produktcode.");
            return new JsonObject { ["key"] = id, ["secret"] = secret, ["site"] = site, ["product"] = product }.ToJsonString();
        }
        return ApiAccountSource.EncodeCredential(vendor, Value("key"), Value("team"));
    }
    internal static JsonObject Google(string project, string exportTable)
    {
        project = project.Trim(); exportTable = exportTable.Trim();
        if (!Regex.IsMatch(project, "^[a-z][a-z0-9-]{4,61}[a-z0-9]$")) throw new ArgumentException("Bitte eine gültige Google-Projekt-ID eingeben.");
        if (exportTable.Length > 0 && !Regex.IsMatch(exportTable, "^[a-z][a-z0-9-]{4,61}[a-z0-9]\\.[A-Za-z0-9_]{1,1024}\\.[A-Za-z0-9_]{1,1024}$"))
            throw new ArgumentException("Exporttabelle muss projekt.dataset.tabelle entsprechen.");
        return new() { ["ProjectId"] = project, ["BillingExport"] = exportTable };
    }
}
