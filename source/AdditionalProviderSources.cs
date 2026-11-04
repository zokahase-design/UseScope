using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class ArceeSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal ArceeSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _send = send ?? _http.Send; _read = read ?? SecretVault.Read; }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var key = await _read(identity, cancellation); if (key is null) return new(identity.Key, [], null, "Arcee-API-Schlüssel hinterlegen", null, true);
        var end = DateTimeOffset.UtcNow; var start = new DateTimeOffset(end.Year, end.Month, 1, 0, 0, 0, TimeSpan.Zero);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.arcee.ai/api/v1/usage/summary?start_date=" + Uri.EscapeDataString(start.ToString("O")) + "&end_date=" + Uri.EscapeDataString(end.ToString("O")));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Providers.ValidateKey(key));
        return Parse(identity, await _send(request, cancellation), start, end);
    }
    internal static Reading Parse(Identity identity, JsonObject value, DateTimeOffset start, DateTimeOffset end)
    {
        var input = MeterData.Count(value["total_input_tokens"]); var output = MeterData.Count(value["total_output_tokens"]); var total = MeterData.Count(value["total_tokens"]); var calls = MeterData.Count(value["total_requests"]);
        if (checked(input + output) != total) throw new InvalidDataException("Arcee-Token-Summe inkonsistent.");
        return new(identity.Key, [], null, "API verbunden · Arcee-Verbrauch", DateTimeOffset.UtcNow,
            Detail: "Monat · " + total.ToString("N0", ApiAccountSource.German) + " Token · " + calls.ToString("N0", ApiAccountSource.German) + " Anfragen"
            + "\nInput " + input.ToString("N0", ApiAccountSource.German) + " · Output " + output.ToString("N0", ApiAccountSource.German)
            + "\nArcee-Kontobericht einschließlich der dort erfassten API-/App-Nutzung; keine Kosten oder Wallet aus Tokenzahlen abgeleitet."
            + "\nAbfragezeitraum " + start.ToString("u") + " bis " + end.ToString("u") + ". Anbieter-Verzögerung möglich.",
            Tokens: new(total, start, end, "Arcee-Kontobericht · dort erfasste API-/App-Nutzung; Anbieter-Verzögerung möglich.", input, output));
    }
    public void Dispose() => _http.Dispose();
}

internal sealed class StepFunSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal StepFunSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _send = send ?? _http.Send; _read = read ?? SecretVault.Read; }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var key = await _read(identity, cancellation); if (key is null) return new(identity.Key, [], null, "Internationalen StepFun-API-Schlüssel hinterlegen", null, true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.stepfun.ai/v1/accounts"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Providers.ValidateKey(key));
        return Parse(identity, await _send(request, cancellation));
    }
    internal static Reading Parse(Identity identity, JsonObject response)
    {
        if (JsonData.Text(response["object"]) != "account" || JsonData.Text(response["type"]) is not ("prepaid" or "postpaid")) throw new InvalidDataException("StepFun-Kontoantwort ungültig.");
        var balance = MeterData.Decimal(response["balance"]);
        return new(identity.Key, [], "Saldo " + MeterData.Money(balance, "USD") + "*", "API verbunden · StepFun International", DateTimeOffset.UtcNow,
            Detail: "Kontosaldo " + MeterData.Money(balance, "USD") + "* · " + JsonData.Text(response["type"])
            + "\n* USD gemäß internationaler Anbieterpreisliste; im API-Response steht kein Währungscode. Keine Umrechnung."
            + "\nStandard-API-Konto, kein Step-Plan-Restkontingent. total_cash_balance und total_voucher_balance sind Gesamteinzahlungen/Gesamtboni und werden nicht als aktuelles Guthaben addiert.", DetailContainsBalance: true);
    }
    public void Dispose() => _http.Dispose();
}

internal sealed class MistralAdminSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal MistralAdminSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _send = send ?? _http.Send; _read = read ?? SecretVault.Read; }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var key = await _read(identity, cancellation); if (key is null) return new(identity.Key, [], null, "Mistral-Enterprise-Admin-Key erforderlich", null, true);
        var end = DateTimeOffset.UtcNow; var start = new DateTimeOffset(end.Year, end.Month, 1, 0, 0, 0, TimeSpan.Zero); var offset = 0; long? total = null;
        for (var page = 0; page < 64; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/admin/usage/query");
            request.Headers.Add("x-api-key", Providers.ValidateKey(key));
            request.Content = new StringContent(new JsonObject {
                ["start"] = start.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), ["end"] = end.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["group_by"] = new JsonArray("billing_metric", "billing_group"), ["include_aggregates"] = true, ["include_nb_api_calls"] = true, ["limit"] = 1000, ["offset"] = offset
            }.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await _send(request, cancellation);
            if (response["groups"] is not JsonArray rows || response["metadata"] is not JsonObject meta) throw new InvalidDataException("Mistral-Usagebericht fehlt.");
            var calls = response["aggregates"]?["total_api_calls"] is null ? (long?)null : MeterData.Count(response["aggregates"]?["total_api_calls"]);
            if (page > 0 && total != calls) throw new InvalidDataException("Mistral-Usage-Snapshot änderte sich."); total = calls;
            if (!MeterData.More(meta)) return new(identity.Key, [], null, total is null ? "Enterprise-API · keine Aufrufsumme gemeldet" : "Enterprise-API verbunden · Mistral", total is null ? null : DateTimeOffset.UtcNow,
                Detail: total is null ? "Keine gemeldete API-Aufrufsumme; keine Nullzahl erfunden." : "Monat · " + total.Value.ToString("N0", ApiAccountSource.German) + " gemeldete API-Aufrufe"
                    + "\nEnterprise-Admin-Beta. Keine Token-/Kostensumme aus heterogenen Billing-Metriken abgeleitet. Kein Le-Chat-Abo und kein Wallet."
                    + "\nZeitraum " + start.ToString("u") + " bis " + end.ToString("u"));
            if (rows.Count == 0) throw new InvalidDataException("Mistral-Pagination unvollständig."); offset = checked(offset + rows.Count);
        }
        throw new InvalidDataException("Mistral-Pagination zu lang.");
    }
    public void Dispose() => _http.Dispose();
}
