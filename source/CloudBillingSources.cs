using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class DigitalOceanBillingSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal DigitalOceanBillingSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _send = send ?? _http.Send; _read = read ?? SecretVault.Read; }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var key = await _read(identity, cancellation);
        if (key is null) return new(identity.Key, [], null, "DigitalOcean billing:read-Token hinterlegen", null, true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.digitalocean.com/v2/customers/my/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Providers.ValidateKey(key));
        return Parse(identity, await _send(request, cancellation));
    }
    internal static Reading Parse(Identity identity, JsonObject value)
    {
        var observed = JsonData.Time(value["generated_at"]) ?? throw new InvalidDataException("DigitalOcean-Abrechnungsstand fehlt.");
        var balance = MeterData.Decimal(value["account_balance"]); var usage = MeterData.Decimal(value["month_to_date_usage"]); var current = MeterData.Decimal(value["month_to_date_balance"]);
        return new(identity.Key, [], "Saldo " + MeterData.Money(current, "USD") + "*", "Cloud-Abrechnung · DigitalOcean", observed,
            Detail: "Cloud-Monat · " + MeterData.Money(usage, "USD") + "* Verbrauch"
                + "\nKontosaldo inkl. laufendem Monat: " + MeterData.Money(current, "USD") + "* · Vorzeichen unverändert. Kein als positiv umgedeutetes Guthaben."
                + "\nLetzter Rechnungssaldo ohne laufenden Monat: " + MeterData.Money(balance, "USD") + "*."
                + "\nGesamtes Cloudkonto einschließlich anderer Dienste; kein alleiniger Inference-Verbrauch und keine Tokenzahl. * USD gemäß DigitalOcean-Abrechnung, nicht als Währungsfeld in der Antwort gemeldet. Datenstand " + observed.ToString("u"), DetailContainsBalance: true);
    }
    public void Dispose() => _http.Dispose();
}
