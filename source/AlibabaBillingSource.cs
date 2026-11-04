using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class AlibabaBillingSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string?>> _read;
    internal AlibabaBillingSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string?>>? read = null)
    { _send = send ?? _http.Send; _read = read ?? SecretVault.Read; }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var stored = await _read(identity, cancellation);
        if (stored is null) return new(identity.Key, [], null, "RAM-Lesezugang hinterlegen", null, true);
        var keys = NativeConnections.Object(stored); var id = Providers.ValidateKey(JsonData.Text(keys["key"]) ?? ""); var secret = Providers.ValidateKey(JsonData.Text(keys["secret"]) ?? "");
        var site = JsonData.Text(keys["site"]); var host = site switch { "global" => "business.ap-southeast-1.aliyuncs.com", "cn" => "business.aliyuncs.com", _ => throw new InvalidDataException("Abrechnungsregion fehlt.") };
        var product = JsonData.Text(keys["product"]) ?? "";
        // Validate persisted fields again, not just the configuration dialog.
        ConnectionSetup.Encode(identity.Vendor, new Dictionary<string, string> { ["key"] = id, ["secret"] = secret, ["site"] = site!, ["product"] = product });
        var wallet = await _send(Sign(host, "QueryAccountBalance", [], id, secret, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N")), cancellation);
        var cycle = DateTimeOffset.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["BillingCycle"] = cycle };
        if (product.Length > 0) parameters["ProductCode"] = product;
        JsonObject bill;
        try { bill = await _send(Sign(host, "QueryBillOverview", parameters, id, secret, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N")), cancellation); }
        catch (RemoteFailure error) when (error.Authentication) { bill = new JsonObject { ["Success"] = false }; }
        return Parse(identity, wallet, bill, cycle, product);
    }
    internal static HttpRequestMessage Sign(string host, string action, IEnumerable<KeyValuePair<string, string>> parameters, string id, string secret, DateTimeOffset now, string nonce)
    {
        if (host is not ("business.ap-southeast-1.aliyuncs.com" or "business.aliyuncs.com") || action is not ("QueryAccountBalance" or "QueryBillOverview")) throw new ArgumentException();
        var query = string.Join("&", parameters.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value)));
        static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var payload = Hash("");
        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["host"] = host, ["x-acs-action"] = action,
            ["x-acs-version"] = "2017-12-14", ["x-acs-date"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["x-acs-signature-nonce"] = nonce, ["x-acs-content-sha256"] = payload };
        var names = string.Join(";", headers.Keys); var canonical = "GET\n/\n" + query + "\n" + string.Join("", headers.Select(item => item.Key + ":" + item.Value + "\n")) + "\n" + names + "\n" + payload;
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("ACS3-HMAC-SHA256\n" + Hash(canonical))));
        var request = new HttpRequestMessage(HttpMethod.Get, "https://" + host + "/" + (query.Length > 0 ? "?" + query : ""));
        foreach (var header in headers) request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        request.Headers.TryAddWithoutValidation("Authorization", "ACS3-HMAC-SHA256 Credential=" + id + ",SignedHeaders=" + names + ",Signature=" + signature);
        return request;
    }
    internal static Reading Parse(Identity identity, JsonObject wallet, JsonObject bill, string cycle, string product)
    {
        if (!JsonData.Flag(wallet["Success"])) throw new RemoteFailure("Alibaba-Abrechnungsrechte oder Antwort prüfen");
        var currency = MeterData.Currency(JsonData.Text(wallet["Data"]?["Currency"]));
        var cash = MeterData.Decimal(wallet["Data"]?["AvailableCashAmount"]);
        var data = bill["Data"] as JsonObject;
        if (data?["BillingCycle"] is not null && JsonData.Text(data["BillingCycle"]) != cycle) throw new InvalidDataException("Abrechnungszeitraum passt nicht.");
        var items = data?["Items"]?["Item"];
        if (items is not null && items is not JsonArray) throw new InvalidDataException("Abrechnungspositionen ungültig.");
        var rows = items as JsonArray ?? [];
        if (rows.Count > 0 && JsonData.Text(data?["BillingCycle"]) != cycle) throw new InvalidDataException("Abrechnungszeitraum fehlt.");
        var totals = new Dictionary<string, decimal>();
        foreach (var row in rows)
        {
            if (product.Length > 0 && JsonData.Text(row?["ProductCode"]) != product) throw new InvalidDataException("Abrechnungsprodukt passt nicht.");
            var unit = MeterData.Currency(JsonData.Text(row?["Currency"])); totals[unit] = checked(totals.GetValueOrDefault(unit) + MeterData.Decimal(row?["PretaxAmount"]));
        }
        var cost = !JsonData.Flag(bill["Success"]) ? "Kostenbericht nicht verfügbar; separate Billing-Leserechte prüfen" : totals.Count > 0 ? string.Join(" · ", totals.Select(item => MeterData.Money(item.Value, item.Key))) : "keine Kostenpositionen gemeldet";
        return new(identity.Key, [], MeterData.Money(cash, currency), !JsonData.Flag(bill["Success"]) ? "Cloud-Wallet verbunden · Kostenrechte fehlen" : "API verbunden · Alibaba-Abrechnung", DateTimeOffset.UtcNow,
            Detail: cycle + " · " + cost + (product.Length == 0 ? " · gesamtes Cloudkonto" : " · Produkt " + product)
            + "\nKosten nach Rabatten, vor Steuern (PretaxAmount); Abrechnungsnachlauf möglich. Guthaben ist AvailableCashAmount des gemeinsamen Cloudkontos, kein Kreditlimit."
            + "\nQwen/Wan können dieselbe Cloud-Abrechnung teilen. Nicht als zwei getrennte Wallets addieren. Keine aus Kosten abgeleiteten Tokenzahlen.");
    }
    public void Dispose() => _http.Dispose();
}
