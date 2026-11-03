using System.Globalization;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class ApiAccountSource : IAccountSource
{
    private readonly JsonTransport _transport = new();
    private readonly Func<string, string, CancellationToken, Task<JsonObject>> _get;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal ApiAccountSource(Func<string, string, CancellationToken, Task<JsonObject>>? get = null,
        Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _get = get ?? ((url, key, token) => _transport.Get(url, key, false, token)); _read = read ?? SecretVault.Read; }
    internal static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    internal static string Money(double amount, string currency) => amount.ToString("N2", German) + " " + currency;
    private static double Required(JsonNode? value) => JsonData.Number(value) ?? throw new InvalidDataException("Unvollständige Abrechnungsdaten.");
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var provider = Providers.Get(identity.Vendor);
        if (provider.Connection == ConnectionKind.Portal)
            return new(identity.Key, [], null, "Portal · keine automatische Messung", null, Detail: provider.Help);
        var stored = await _read(identity, cancellation);
        if (stored is null) return new(identity.Key, [], null, "API-Schlüssel hinterlegen", null, true, Detail: provider.Help);
        string? team = null;
        var key = stored;
        if (identity.Vendor == "xai")
        {
            var credential = JsonNode.Parse(stored)?.AsObject() ?? throw new InvalidDataException();
            key = JsonData.Text(credential["key"]) ?? "";
            if (!Guid.TryParse(JsonData.Text(credential["team"]), out var id)) throw new InvalidDataException("Team-ID fehlt.");
            team = id.ToString("D");
        }
        key = Providers.ValidateKey(key);
        try
        {
            return identity.Vendor switch {
                "deepseek" => ParseDeepSeek(identity, await _get("https://api.deepseek.com/user/balance", key, cancellation)),
                "moonshot" => ParseMoonshot(identity, await _get("https://api.moonshot.ai/v1/users/me/balance", key, cancellation)),
                "openrouter" => ParseOpenRouter(identity, await _get("https://openrouter.ai/api/v1/key", key, cancellation)),
                "openai-api" => await OpenAiUsage(identity, key, cancellation),
                "xai" => ParseXai(identity, await _get("https://management-api.x.ai/v1/billing/teams/" + team + "/prepaid/balance", key, cancellation)),
                _ => throw new InvalidOperationException("Unbekannte API-Anbindung.")
            };
        }
        catch (RemoteFailure failure) when (failure.Authentication)
        { throw new RemoteFailure("API-Schlüssel oder Berechtigung prüfen", authentication: true); }
    }
    internal static string EncodeCredential(string vendor, string key, string team)
    {
        key = Providers.ValidateKey(key);
        if (vendor != "xai") return key;
        if (!Guid.TryParse(team.Trim(), out var id)) throw new ArgumentException("Bitte die gültige xAI-Team-ID (UUID) aus dem Anbieterportal eingeben.");
        return new JsonObject { ["key"] = key, ["team"] = id.ToString("D") }.ToJsonString();
    }
    internal static Reading ParseXai(Identity identity, JsonObject response)
    {
        if (!decimal.TryParse(JsonData.Text(response["total"]?["val"]), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var cents)) throw new InvalidDataException("xAI meldet keinen Teamsaldo.");
        var money = (cents / 100m).ToString("N2", German) + " USD";
        return new(identity.Key, [], "Saldo " + money, "API verbunden · Team", DateTimeOffset.Now,
            Detail: "Teamsaldo " + money + "\nVorzeichen wie von xAI gemeldet; kein als positiv normalisiertes Guthaben.\nUSD-Cents aus total.val, geteilt durch 100. Keine Token- oder Kostensumme. Grok-/X-Abos sind getrennt.", DetailContainsBalance: true);
    }
    internal static Reading ParseDeepSeek(Identity identity, JsonObject response)
    {
        var balances = (response["balance_infos"] as JsonArray)?.OfType<JsonObject>().Select(item =>
            (Currency: JsonData.Text(item["currency"]), Amount: JsonData.Number(item["total_balance"])))
            .Where(item => item.Currency is "USD" or "CNY" && item.Amount is not null).ToArray() ?? [];
        if (balances.Length == 0) throw new InvalidDataException("Kein gemeldetes Guthaben.");
        var details = "API-Guthaben: " + string.Join(" · ", balances.Select(item => Money(item.Amount!.Value, item.Currency!)));
        var selected = balances.FirstOrDefault(item => item.Currency == "USD"); if (selected.Currency is null) selected = balances[0];
        var restricted = response["is_available"] is JsonValue flag && flag.TryGetValue<bool>(out var available) && !available;
        return new(identity.Key, [], Money(selected.Amount!.Value, selected.Currency!), restricted ? "API-Konto nicht verfügbar" : "API verbunden",
            DateTimeOffset.Now, restricted, Detail: details + "\nKeine kontoweite Token- oder Kostensumme über diesen Endpunkt.", DetailContainsBalance: true);
    }
    internal static Reading ParseMoonshot(Identity identity, JsonObject response)
    {
        if (!JsonData.Flag(response["status"])) throw new InvalidDataException("Kimi bestätigt die Guthabenabfrage nicht.");
        var balance = Required(response["data"]?["available_balance"]);
        return new(identity.Key, [], Money(balance, "USD"), "API verbunden", DateTimeOffset.Now,
            Detail: "Internationales API-Guthaben · USD\nKeine kontoweite Token- oder Kostensumme über diesen Endpunkt.");
    }
    internal static Reading ParseOpenRouter(Identity identity, JsonObject response)
    {
        var data = response["data"] as JsonObject ?? throw new InvalidDataException();
        var total = Required(data["usage"]);
        var details = new List<string> { "API-Schlüssel · Gesamtverbrauch " + Money(total, "USD") };
        if (JsonData.Number(data["usage_monthly"]) is double month) details.Insert(0, "Monat " + Money(month, "USD") + " · API-Schlüssel");
        if (JsonData.Number(data["usage_daily"]) is double day) details.Add("Heute " + Money(day, "USD"));
        if (JsonData.Number(data["usage_weekly"]) is double week) details.Add("Woche " + Money(week, "USD"));
        var remaining = JsonData.Number(data["limit_remaining"]);
        details.Add("Restlimit ist das Ausgabenbudget dieses Schlüssels, kein Kontoguthaben. Keine Tokenhistorie.");
        if (JsonData.Text(data["limit_reset"]) is string reset) details.Add("Budget-Reset: " + reset);
        return new(identity.Key, [], remaining is double amount ? "Rest " + Money(amount, "USD") : null,
            "API verbunden · Schlüsselverbrauch", DateTimeOffset.Now, Detail: string.Join("\n", details));
    }
    private async Task<Reading> OpenAiUsage(Identity identity, string key, CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow; var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var query = "?start_time=" + start.ToUnixTimeSeconds() + "&end_time=" + now.ToUnixTimeSeconds() + "&bucket_width=1d&limit=31";
        var usage = await Pages("https://api.openai.com/v1/organization/usage/completions" + query, key, cancellation);
        var costs = await Pages("https://api.openai.com/v1/organization/costs" + query, key, cancellation);
        return ParseOpenAi(identity, usage, costs, start);
    }
    private async Task<JsonArray> Pages(string url, string key, CancellationToken cancellation)
    {
        var combined = new JsonArray(); string? page = null; var seen = new HashSet<string>();
        for (var index = 0; index < 32; index++)
        {
            var response = await _get(url + (page is null ? "" : "&page=" + Uri.EscapeDataString(page)), key, cancellation);
            if (response["data"] is not JsonArray buckets) throw new InvalidDataException("Unvollständige Abrechnungsdaten.");
            foreach (var bucket in buckets) combined.Add(bucket?.DeepClone());
            if (response["has_more"] is not JsonValue flag || !flag.TryGetValue<bool>(out var more)) throw new InvalidDataException("Pagination nicht bestätigt.");
            if (!more) return combined;
            page = JsonData.Text(response["next_page"]);
            if (string.IsNullOrEmpty(page) || !seen.Add(page)) throw new InvalidDataException("Ungültige Abrechnungsseiten.");
        }
        throw new InvalidDataException("Abrechnungsabfrage unvollständig.");
    }
    internal static Reading ParseOpenAi(Identity identity, JsonArray usage, JsonArray costs, DateTimeOffset start)
    {
        long input = 0, output = 0; var totals = new Dictionary<string, decimal>();
        long Tokens(JsonNode? node) => node is JsonValue number && number.TryGetValue<long>(out var value) && value >= 0 ? value : throw new InvalidDataException("Ungültige Tokenzahl.");
        IEnumerable<JsonObject> Results(JsonArray buckets)
        {
            foreach (var bucket in buckets)
            {
                if (bucket?["results"] is not JsonArray rows) throw new InvalidDataException();
                foreach (var row in rows) yield return row as JsonObject ?? throw new InvalidDataException();
            }
        }
        foreach (var item in Results(usage)) { input = checked(input + Tokens(item["input_tokens"])); output = checked(output + Tokens(item["output_tokens"])); }
        foreach (var item in Results(costs))
        {
            var currency = JsonData.Text(item["amount"]?["currency"])?.ToUpperInvariant() ?? throw new InvalidDataException();
            if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)) throw new InvalidDataException();
            var value = item["amount"]?["value"];
            if (!decimal.TryParse(JsonData.Text(value) ?? value?.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)) throw new InvalidDataException("Ungültiger Kostenbetrag.");
            totals[currency] = checked(totals.GetValueOrDefault(currency) + amount);
        }
        var cost = totals.Count == 0 ? "keine gemeldeten Kosten" : string.Join(" · ", totals.Select(item => item.Value.ToString("N2", German) + " " + item.Key));
        return new(identity.Key, [], null, "API verbunden · Organisation", DateTimeOffset.Now,
            Detail: "Monat " + cost + " · " + checked(input + output).ToString("N0", German) + " Text-Token\nText-Input " + input.ToString("N0", German) + " Token · Output " + output.ToString("N0", German)
                + " Token\nOrganisation · seit " + start.ToString("dd.MM.yyyy") + " UTC; Anbieter-Verzögerung möglich.\nKosten: alle API-Kosten. Token: Text-/Completion-Nutzung, keine vollständige Bild-, Video- oder Audiozählung. Kein Guthaben.");
    }
    public void Dispose() => _transport.Dispose();
}
