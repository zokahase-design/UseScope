using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class MeterRuntime : IAsyncDisposable
{
    internal static readonly MeterRuntime Shared = new();
    internal readonly MeterLedger Ledger = new();
    private IReadOnlyList<Identity> _accounts = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LoopbackMeterServer? _server;
    private bool _disposed;
    internal string? Failure { get; private set; }
    internal void Update(IReadOnlyList<Identity> identities) => Volatile.Write(ref _accounts, identities.ToArray());
    internal async Task EnsureStarted(CancellationToken cancellation = default)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_server is not null) return;
            var server = new LoopbackMeterServer(() => Volatile.Read(ref _accounts), Ledger);
            try { await server.Start(cancellation: cancellation); _server = server; Failure = null; }
            catch { await server.DisposeAsync(); Failure = "Lokaler API-Zähler nicht erreichbar. Port 48371 belegt oder Start blockiert."; throw new IOException(Failure); }
        }
        finally { _gate.Release(); }
    }
    internal async Task StartIfNeeded(IReadOnlyList<Identity> identities)
    {
        Update(identities);
        if (!identities.Any(value => value.Enabled && MeterStorage.Enabled(value))) return;
        try { await EnsureStarted(); } catch (IOException) { }
    }
    internal async Task<string> CheckConnection(Identity identity)
    {
        await EnsureStarted();
        var credential = await MeterStorage.ReadCredential(identity, default);
        if (credential is null) return "Bitte zuerst den API-Zähler einrichten.";
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(20) };
        using var request = new HttpRequestMessage(HttpMethod.Get, LoopbackMeterServer.ClientUrl(identity) + "/models");
        request.Headers.Authorization = new("Bearer", credential.LocalToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        return (int)response.StatusCode switch {
            >= 200 and < 300 => "Anbieter über den lokalen Zähler erreichbar; Modellliste angenommen. Es wurde keine Generierung ausgelöst. Dies prüft keinen Kontogesamtverbrauch.",
            404 or 405 => "Dieser Endpunkt bietet keine Modellliste unter /models. Verbindung ohne Generierung nicht abschließend prüfbar. Die erste reguläre Anfrage im KI-Client entscheidet.",
            401 or 403 => "Schlüssel, Anbieterrechte oder gewählte Region prüfen. Keine Generierung ausgelöst.",
            429 => "Anbieter oder lokaler Zähler meldet ein Anfrage-Limit. Später erneut prüfen.",
            _ => "Verbindung nicht bestätigt (HTTP " + (int)response.StatusCode + "). Adresse, Region und Header prüfen. Keine Generierung ausgelöst."
        };
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { _disposed = true; if (_server is not null) await _server.DisposeAsync(); _server = null; }
        finally { _gate.Release(); }
    }
}

internal static class MeterSetup
{
    internal const string Help = "Zählt nur API-Anfragen, die du über die lokale UseScope-Adresse leitest. Kein gesamtes Konto oder Abo. Eigenen Inference-Key verwenden; niemals einen Admin-/Billing-Key. HTTPS-Anbieteradresse und Protokoll müssen zu deinem KI-Client passen. UseScope muss während der Nutzung laufen. Keine Prompts oder Antworten auf Disk.";
    internal static JsonObject Options() => new() { ["Mode"] = "meter" };
    internal static IReadOnlyList<ConnectionField> Fields(string vendor)
    {
        var endpoint = ProviderCatalog.Endpoint(vendor);
        return [new("key", "Inference-API-Schlüssel (kein Admin-Key)"), new("upstream", "HTTPS-Basisadresse laut Anbieter-Dokumentation", false, endpoint.Base),
            new("auth", "Auth-Header laut Anbieter (Vorgabe nur bei Bedarf ändern)", false, endpoint.Auth)];
    }
    internal static MeterCredential Encode(IReadOnlyDictionary<string, string> values, string? localToken = null)
    {
        var key = Providers.ValidateKey(values.GetValueOrDefault("key") ?? "");
        var upstream = MeterNetwork.ValidateBase(values.GetValueOrDefault("upstream") ?? "").AbsoluteUri;
        var auth = values.GetValueOrDefault("auth")?.Trim().ToLowerInvariant();
        if (auth is not ("bearer" or "x-api-key" or "api-key" or "x-goog-api-key" or "api-subscription-key")) throw new ArgumentException("Zulässig: bearer, x-api-key, api-key, x-goog-api-key oder api-subscription-key.");
        return new(key, localToken ?? "usm_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), upstream, auth);
    }
}
