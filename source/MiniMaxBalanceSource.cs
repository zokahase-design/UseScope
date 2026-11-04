using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class MiniMaxBalanceSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal MiniMaxBalanceSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _send = send ?? _http.Send; _read = read ?? SecretVault.Read; }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var stored = await _read(identity, cancellation); if (stored is null) return new(identity.Key, [], null, "MiniMax-PAYG-Schlüssel hinterlegen", null, true);
        var values = NativeConnections.Object(stored); var key = Providers.ValidateKey(JsonData.Text(values["key"]) ?? "");
        var currency = MeterData.Currency(JsonData.Text(values["currency"])); if (currency is not ("USD" or "CNY")) throw new InvalidDataException();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.minimax.io/account/query_balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return Parse(identity, await _send(request, cancellation), currency);
    }
    internal static Reading Parse(Identity identity, JsonObject response, string configuredCurrency)
    {
        if (MeterData.Decimal(response["base_resp"]?["status_code"]) != 0) throw new RemoteFailure("MiniMax-PAYG-Zugang prüfen");
        var amount = MeterData.Decimal(response["available_amount"]); MeterData.Currency(configuredCurrency);
        var money = MeterData.Money(amount, configuredCurrency);
        return new(identity.Key, [], money + "*", "API verbunden · MiniMax PAYG", DateTimeOffset.UtcNow,
            Detail: "Verfügbarer Betrag " + money + "*\n* Kontowährung vom Nutzer eingestellt; die API meldet keine Währung. Betrag aus available_amount, keine Einheit umgerechnet."
            + "\nGlobales MiniMax-PAYG-Konto (z. B. H3); getrennt vom M-Plan und Hailuo-App-Abo. Keine Token- oder Video-Anzahl aus Guthabenänderungen abgeleitet.", DetailContainsBalance: true);
    }
    public void Dispose() => _http.Dispose();
}
