using System.Globalization;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class AnthropicAnalyticsSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal AnthropicAnalyticsSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _send = send ?? _http.Send; _read = read ?? SecretVault.Read; }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var key = await _read(identity, cancellation);
        if (key is null) return new(identity.Key, [], null, "Admin-API-Schlüssel hinterlegen", null, true);
        key = Providers.ValidateKey(key);
        var now = DateTimeOffset.UtcNow; var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        if (end <= start) return new(identity.Key, [], null, "Noch kein abgeschlossenes UTC-Stundenfenster", null);
        var query = "?starting_at=" + Uri.EscapeDataString(start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
            + "&ending_at=" + Uri.EscapeDataString(end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)) + "&limit=1000";
        var usage = await Pages("usage", query, key, cancellation); var cost = await Pages("cost", query, key, cancellation);
        return Parse(identity, usage.Rows, cost.Rows, start, end, usage.Refreshed, cost.Refreshed);
    }
    private async Task<(JsonArray Rows, DateTimeOffset? Refreshed)> Pages(string resource, string query, string key, CancellationToken cancellation)
    {
        var rows = new JsonArray(); var seen = new HashSet<string>(); string? cursor = null; DateTimeOffset? snapshot = null; var first = true;
        for (var index = 0; index < 64; index++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/organizations/analytics/" + resource + query + (cursor is null ? "" : "&page=" + Uri.EscapeDataString(cursor)));
            request.Headers.Add("x-api-key", key); request.Headers.Add("anthropic-version", "2023-06-01"); request.Headers.Add("anthropic-beta", "analytics-usage-cost-2026-08-24");
            var page = await _send(request, cancellation);
            if (!page.ContainsKey("data_refreshed_at") || page["data"] is not JsonArray data) throw new InvalidDataException("Analytics-Antwort unvollständig.");
            var refreshed = JsonData.Time(page["data_refreshed_at"]);
            if (page["data_refreshed_at"] is not null && refreshed is null) throw new InvalidDataException("Datenstand ungültig.");
            if (!first && snapshot != refreshed) throw new InvalidDataException("Analytics-Snapshot hat sich beim Blättern geändert.");
            first = false; snapshot = refreshed;
            foreach (var row in data) rows.Add(row?.DeepClone());
            if (!MeterData.More(page)) return (rows, snapshot);
            cursor = JsonData.Text(page["next_page"]);
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)) throw new InvalidDataException("Analytics-Pagination unvollständig.");
        }
        throw new InvalidDataException("Zu viele Analytics-Seiten.");
    }
    internal static Reading Parse(Identity identity, JsonArray usage, JsonArray costs, DateTimeOffset start, DateTimeOffset end, DateTimeOffset? usageFresh, DateTimeOffset? costFresh)
    {
        if ((usage.Count > 0 && usageFresh is null) || (costs.Count > 0 && costFresh is null)) throw new InvalidDataException("Daten ohne Datenstand.");
        long tokens = 0, input = 0, output = 0; decimal cents = 0;
        foreach (var row in usage)
        {
            if (row?["usage"] is not JsonObject value) throw new InvalidDataException();
            var uncached = MeterData.Count(value["uncached_input_tokens"]); var cached = MeterData.Count(value["cache_read_input_tokens"]);
            var oneHour = MeterData.Count(value["cache_creation"]?["ephemeral_1h_input_tokens"]); var fiveMinutes = MeterData.Count(value["cache_creation"]?["ephemeral_5m_input_tokens"]);
            var incoming = checked(uncached + cached + oneHour + fiveMinutes); var outgoing = MeterData.Count(value["output_tokens"]);
            var total = MeterData.Count(value["total_tokens"]); if (total != checked(incoming + outgoing)) throw new InvalidDataException("Token-Summe ist inkonsistent.");
            input = checked(input + incoming); output = checked(output + outgoing); tokens = checked(tokens + total);
        }
        foreach (var row in costs)
        {
            if (row?["cost"] is not JsonObject cost || JsonData.Text(cost["currency"]) != "USD") throw new InvalidDataException("Unbekannte Analytics-Währung.");
            cents = checked(cents + MeterData.Decimal(cost["amount"]));
        }
        var costText = costs.Count == 0 ? "keine gemeldeten Kosten" : MeterData.Money(cents / 100m, "USD");
        var tokenText = usage.Count == 0 ? "keine gemeldeten Token" : tokens.ToString("N0", ApiAccountSource.German) + " Token";
        var observed = usageFresh is null ? costFresh : costFresh is null ? usageFresh : usageFresh < costFresh ? usageFresh : costFresh;
        var details = "Monat " + costText + " · " + tokenText + "\nOrganisation · " + start.ToString("dd.MM.") + " bis " + end.ToString("dd.MM. HH:mm") + " UTC (abgeschlossene Stunden)"
            + "\nInput inkl. Cache: " + input.ToString("N0", ApiAccountSource.German) + " · Output: " + output.ToString("N0", ApiAccountSource.German)
            + "\nKosten nach Rabatten, vor Credits; Priority-Vertragsgebühren fehlen. Daten können bis zu 24 h verzögert und später korrigiert sein. Kein Abo-Guthaben."
            + "\nTokenstand: " + (usageFresh?.ToString("u") ?? "nicht gemeldet") + " · Kostenstand: " + (costFresh?.ToString("u") ?? "nicht gemeldet");
        return new(identity.Key, [], null, observed is null ? "Analytics · noch keine Daten" : "API verbunden · Organisationsbericht", observed, Detail: details,
            Tokens: usageFresh is null || usageFresh < start ? null : new(tokens, start, end, "Anthropic-Organisation · API, Input einschließlich Cache; abgeschlossene UTC-Stunden, bis zu 24 h verzögert. Kein Claude-Abo.", input, output, Partial: usageFresh < end));
    }
    public void Dispose() => _http.Dispose();
}
