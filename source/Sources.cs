using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal interface IAccountSource : IDisposable
{
    Task<Reading> Query(Identity identity, CancellationToken cancellation);
}

internal static class Limits
{
    internal static Reading OpenAI(string key, JsonNode data, string? email, bool includeModels = false)
    {
        var periods = new List<Period>();
        var snapshot = data["rateLimits"] as JsonObject;
        string? balance = null;
        void Add(JsonObject value, string prefix)
        {
            foreach (var windowKey in new[] { "primary", "secondary" })
            {
                var window = value[windowKey] as JsonObject;
                var minutes = JsonData.Number(window?["windowDurationMins"]);
                var caption = minutes is > 0 ? minutes >= 1440 ? $"{minutes / 1440:0.#}d" : $"{minutes / 60:0.#}h" : windowKey == "primary" ? "Aktuell" : "Woche";
                var period = JsonData.Limit(prefix + caption, window, "usedPercent", "resetsAt");
                if (period is not null) periods.Add(period);
            }
            if (balance is null && value["credits"] is JsonObject credits)
                balance = JsonData.Flag(credits["unlimited"]) ? "Credits ∞"
                    : JsonData.Number(credits["balance"]) is double number ? "Credits " + (JsonData.Text(credits["balance"])?.Trim()
                        ?? number.ToString("0.################", CultureInfo.InvariantCulture)) : null;
        }
        if (snapshot is not null) Add(snapshot, "");
        if (data["rateLimitsByLimitId"] is JsonObject sets)
            foreach (var (name, value) in sets)
                if (value is JsonObject group && (snapshot is null && name == "codex"
                    || includeModels && name != JsonData.Text(snapshot?["limitId"]) && name != "codex"))
                    Add(group, (JsonData.Text(group["limitName"]) ?? name) + " ");
        return new Reading(key, periods.Distinct().ToArray(), balance, email ?? "Verbunden", DateTimeOffset.Now);
    }
    internal static IReadOnlyList<Period> Anthropic(JsonObject data, bool secondary)
    {
        var result = new List<Period>();
        foreach (var (name, value) in data)
        {
            if (name is not ("five_hour" or "seven_day") && (!secondary || !name.StartsWith("seven_day_", StringComparison.Ordinal))) continue;
            var caption = name == "five_hour" ? "5h" : name == "seven_day" ? "7d" : name[10..].Replace('_', ' ');
            if (JsonData.Limit(caption, value, "utilization", "resets_at") is Period period) result.Add(period);
        }
        return result;
    }
    internal static string? Wallet(JsonObject data)
    {
        var money = (data["balance"] as JsonObject)?["money"] as JsonObject;
        var amount = JsonData.Number(money?["amount_minor"]);
        var currency = JsonData.Text(money?["currency"]);
        var exponent = JsonData.Number(money?["exponent"]);
        if (amount is null || currency is null || exponent is null or < 0 or > 6) return null;
        return (amount.Value / Math.Pow(10, exponent.Value)).ToString("N2", CultureInfo.GetCultureInfo("de-DE"))
            + " " + (currency.ToUpperInvariant() switch { "EUR" => "€", "USD" => "$", "GBP" => "£", _ => currency.ToUpperInvariant() });
    }
}

internal sealed class RemoteFailure(string message, DateTimeOffset? retry = null, bool authentication = false, int? httpStatus = null) : Exception(message)
{
    internal DateTimeOffset? Retry { get; } = retry;
    internal bool Authentication { get; } = authentication;
    internal int? HttpStatus { get; } = httpStatus;
}

internal sealed class JsonTransport : IDisposable
{
    private readonly HttpClient _client;
    internal JsonTransport(HttpMessageHandler? handler = null) => _client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
    internal async Task<JsonObject> Get(string url, string token, bool anthropic, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidOperationException("HTTPS erforderlich.");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("UseScope/2.0");
        request.Headers.Accept.ParseAdd("application/json");
        if (anthropic) request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new RemoteFailure("Anbieter pausiert die Abfrage", response.Headers.RetryAfter?.Date
                ?? DateTimeOffset.Now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5)), httpStatus: 429);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new RemoteFailure("Anmeldung oder Leseberechtigung prüfen (HTTP " + (int)response.StatusCode + ")", authentication: true, httpStatus: (int)response.StatusCode);
        if (!response.IsSuccessStatusCode) throw new RemoteFailure($"Anbieter nicht erreichbar (HTTP {(int)response.StatusCode})", httpStatus: (int)response.StatusCode);
        await response.Content.LoadIntoBufferAsync(2_000_000, cancellation);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation)) as JsonObject
            ?? throw new InvalidDataException("Ungültige Anbieterantwort.");
    }
    public void Dispose() => _client.Dispose();
}

internal sealed class ClaudeSource : IAccountSource
{
    private readonly JsonTransport _transport;
    private readonly Func<string, CancellationToken, Task<JsonObject?>> _credential;
    private readonly Func<Identity, CancellationToken, Task> _authStatus;
    private DateTimeOffset _nextWallet, _nextRenew;
    private string? _wallet;
    private DateTimeOffset? _walletObserved;
    private string? _walletNote;
    private string? _boundAccount, _accessFingerprint;
    internal ClaudeSource(JsonTransport? transport = null, Func<string, CancellationToken, Task<JsonObject?>>? credential = null,
        Func<Identity, CancellationToken, Task>? authStatus = null)
    {
        _transport = transport ?? new(); _credential = credential ?? CredentialAccess.Claude;
        _authStatus = authStatus ?? (async (account, token) => { await NativeConnections.Read(account, token, "auth", "status", "--json"); });
    }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var oauth = await _credential(identity.Profile, cancellation);
        var expires = JsonData.Number(oauth?["expiresAt"]);
        async Task<bool> Recheck()
        {
            if (!identity.AutoRenew || _nextRenew > DateTimeOffset.UtcNow) return false;
            _nextRenew = DateTimeOffset.UtcNow.AddMinutes(15);
            var previous = JsonData.Text(oauth?["accessToken"]);
            try { await _authStatus(identity, cancellation); }
            catch (Exception error) when (error is RemoteFailure or IOException or InvalidOperationException) { }
            oauth = await _credential(identity.Profile, cancellation);
            return JsonData.Text(oauth?["accessToken"]) is string updated && updated != previous;
        }
        if (identity.AutoRenew && expires is > 0 && expires < DateTimeOffset.Now.AddMinutes(5).ToUnixTimeMilliseconds() && _nextRenew < DateTimeOffset.Now)
        {
            await Recheck();
        }
        var token = JsonData.Text(oauth?["accessToken"]);
        if (string.IsNullOrEmpty(token)) throw new RemoteFailure("Claude-Anmeldung erforderlich", authentication: true);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
        if (_accessFingerprint is not null && _accessFingerprint != fingerprint) { _wallet = null; _walletObserved = null; _walletNote = null; _nextWallet = default; }
        _accessFingerprint = fingerprint;
        if (JsonData.Number(oauth?["expiresAt"]) is double expiry && expiry > 0 && expiry <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new RemoteFailure("Claude-Anmeldung abgelaufen; über Anmelden erneuern", authentication: true, httpStatus: 401);
        JsonObject response;
        try { response = await _transport.Get("https://api.anthropic.com/api/oauth/usage", token, true, cancellation); }
        catch (RemoteFailure failure) when (failure.HttpStatus == 401)
        {
            if (!await Recheck()) throw;
            token = JsonData.Text(oauth?["accessToken"]) ?? throw new RemoteFailure("Claude erneut anmelden", authentication: true);
            response = await _transport.Get("https://api.anthropic.com/api/oauth/usage", token, true, cancellation);
        }
        JsonObject? account = null;
        int? supplementaryStatus = null; DateTimeOffset? supplementaryRetry = null;
        var meta = Path.Combine(identity.Profile, ".claude.json");
        if (File.Exists(meta))
        {
            try { account = (JsonNode.Parse(File.ReadAllText(meta)) as JsonObject)?["oauthAccount"] as JsonObject; }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException) { }
        }
        var binding = LocalTokenReader.Binding(account);
        if (_boundAccount is not null && _boundAccount != binding) { _wallet = null; _walletObserved = null; _walletNote = null; _nextWallet = default; }
        _boundAccount = binding;
        if (identity.ShowBalance && _nextWallet < DateTimeOffset.Now && JsonData.Text(account?["organizationUuid"]) is string organization
            && Guid.TryParse(organization, out _))
        {
            _nextWallet = DateTimeOffset.Now.AddMinutes(15);
            try
            {
                var wallet = await _transport.Get("https://api.anthropic.com/api/oauth/organizations/" + organization + "/prepaid/credits", token, true, cancellation);
                _wallet = Limits.Wallet(wallet);
                _walletObserved = _wallet is not null ? DateTimeOffset.UtcNow : null; _walletNote = null;
            }
            catch (RemoteFailure failure)
            {
                if (failure.Retry is DateTimeOffset retry && retry > _nextWallet) _nextWallet = retry;
                if (failure.HttpStatus == 429) { supplementaryStatus = 429; supplementaryRetry = failure.Retry; }
                _walletNote = "Guthabenabfrage pausiert; letzter bekannter Guthabenstand bleibt erhalten.";
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { _walletNote = "Guthaben derzeit nicht aktualisierbar."; }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException) { _walletNote = "Guthaben derzeit nicht aktualisierbar."; }
        }
        return new Reading(identity.Key, Limits.Anthropic(response, identity.Secondary), identity.ShowBalance ? _wallet : null,
            JsonData.Text(account?["emailAddress"]) ?? "Verbunden", DateTimeOffset.Now,
            Detail: "Claude-Abolimits · Abfrage höchstens alle fünf Minuten.\n" + (_walletNote ?? "")
                + (_walletObserved is DateTimeOffset observed && identity.ShowBalance ? "\nGuthabenstand: " + observed.ToLocalTime().ToString("dd.MM. HH:mm") : ""),
            TokenNote: "Die Claude-Abolimitabfrage meldet keine Tokenzahl. Für lokale Claude-Code-Nutzung eine Tokenquelle diesem Konto zuordnen; Webnutzung bleibt außerhalb der Messung.",
            HttpStatus: supplementaryStatus, NextAttempt: supplementaryRetry, AccountBinding: LocalTokenReader.Binding(account));
    }
    public void Dispose() => _transport.Dispose();
}

internal sealed class NousSource : IAccountSource
{
    private readonly JsonTransport _transport = new();
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var path = Path.Combine(identity.Profile, "auth.json");
        JsonObject? Profile() => File.Exists(path)
            ? (JsonNode.Parse(File.ReadAllText(path))?["providers"] as JsonObject)?["nous"] as JsonObject : null;
        async Task<string?> Renew()
        {
            if (!identity.AutoRenew || JsonData.Text(Profile()?["refresh_token"]) is null) return null;
            using var process = Process.Start(Platform.Cli("nous", identity.Profile, false, "auth", "refresh", "nous"))!;
            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var error = process.StandardError.ReadToEndAsync(cancellation);
            try { await process.WaitForExitAsync(cancellation); await Task.WhenAll(output, error); }
            finally { if (!process.HasExited) process.Kill(true); }
            return process.ExitCode == 0 ? JsonData.Text(Profile()?["access_token"]) : null;
        }
        var token = JsonData.Text(Profile()?["access_token"]) ?? await Renew();
        if (token is null)
            return new Reading(identity.Key, [], null, "Anmeldung erforderlich", null, true);
        JsonObject response;
        try { response = await _transport.Get("https://portal.nousresearch.com/api/oauth/account", token, false, cancellation); }
        catch (RemoteFailure failure) when (failure.Authentication)
        {
            token = await Renew();
            if (token is null) throw;
            response = await _transport.Get("https://portal.nousresearch.com/api/oauth/account", token, false, cancellation);
        }
        var credits = JsonData.Number((response["paid_service_access"] as JsonObject)?["total_usable_credits"]);
        var balance = credits is double amount ? "Credits " + amount.ToString("N2", CultureInfo.GetCultureInfo("de-DE")) : null;
        return new Reading(identity.Key, [], balance, JsonData.Text((response["user"] as JsonObject)?["email"]) ?? "Verbunden", DateTimeOffset.Now);
    }
    public void Dispose() => _transport.Dispose();
}
