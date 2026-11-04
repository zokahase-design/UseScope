using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class BillingTransport : IDisposable
{
    private readonly HttpClient _client;
    internal BillingTransport(HttpMessageHandler? handler = null) => _client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
    internal async Task<JsonObject> Send(HttpRequestMessage request, CancellationToken cancellation)
    {
        using (request)
        {
            if (request.RequestUri is not { Scheme: "https" } url || url.UserInfo.Length != 0) throw new InvalidOperationException("HTTPS erforderlich.");
            request.Headers.UserAgent.ParseAdd("UseScope/2.2"); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new RemoteFailure("Anbieter pausiert die Abfrage (HTTP 429)", response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5)), httpStatus: 429);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new RemoteFailure("Zugang oder Leseberechtigung prüfen", authentication: true, httpStatus: (int)response.StatusCode);
            if (!response.IsSuccessStatusCode) throw new RemoteFailure("Abrechnungsabfrage fehlgeschlagen (HTTP " + (int)response.StatusCode + ")", httpStatus: (int)response.StatusCode);
            await response.Content.LoadIntoBufferAsync(2_000_000, cancellation);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation)) as JsonObject ?? throw new InvalidDataException("Ungültige Anbieterantwort.");
        }
    }
    public void Dispose() => _client.Dispose();
}

internal static class MeterData
{
    internal static decimal Decimal(JsonNode? node) => decimal.TryParse(JsonData.Text(node) ?? node?.ToJsonString(), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : throw new InvalidDataException("Messwert fehlt oder ist ungültig.");
    internal static long Count(JsonNode? node)
    {
        var count = Decimal(node); if (count < 0 || count != decimal.Truncate(count)) throw new InvalidDataException("Ungültiger Zähler."); return checked((long)count);
    }
    internal static string Money(decimal value, string currency) => value.ToString("N2", ApiAccountSource.German) + " " + Currency(currency);
    internal static string Currency(string? value) => value is { Length: 3 } && value.All(char.IsAsciiLetterUpper) ? value : throw new InvalidDataException("Währung fehlt.");
    internal static bool More(JsonObject page) => page["has_more"] is JsonValue flag && flag.TryGetValue<bool>(out var more) ? more : throw new InvalidDataException("Pagination fehlt.");
    internal static DateTimeOffset? Milliseconds(JsonNode? value)
    {
        if (value is null) return null;
        try { return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)Decimal(value))); } catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException) { throw new InvalidDataException("Reset-Zeit ungültig."); }
    }
}
