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
                    : JsonData.Text(credits["balance"]) is string amount ? $"Credits {amount}" : null;
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

internal sealed class CodexSource : IAccountSource
{
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        if (!File.Exists(Path.Combine(identity.Profile, "auth.json")))
            return new Reading(identity.Key, [], null, "Anmeldung erforderlich", null, true);
        var info = Platform.Cli("codex", identity.Profile, false, "-c", "cli_auth_credentials_store='file'", "app-server");
        info.Environment.Remove("OPENAI_API_KEY");
        info.Environment.Remove("CODEX_API_KEY");
        using var process = new Process { StartInfo = info };
        process.Start();
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        async Task<JsonObject> Call(int request, string method, JsonNode? parameters)
        {
            var message = new JsonObject { ["id"] = request, ["method"] = method, ["params"] = parameters };
            await process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), cancellation);
            await process.StandardInput.FlushAsync(cancellation);
            while (await process.StandardOutput.ReadLineAsync(cancellation) is string line)
            {
                if (line.Length > 2_000_000) throw new InvalidDataException("Anbieterantwort zu groß.");
                JsonObject? response;
                try { response = JsonNode.Parse(line) as JsonObject; } catch (System.Text.Json.JsonException) { continue; }
                if (response is null || JsonData.Number(response["id"]) != request) continue;
                if (response["error"] is not null) throw new InvalidOperationException("Codex-Abfrage fehlgeschlagen.");
                return response["result"] as JsonObject ?? new JsonObject();
            }
            throw new IOException("Codex-Verbindung wurde beendet.");
        }
        try
        {
            await Call(1, "initialize", new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "usescope_desktop", ["version"] = "2.0" } });
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(), cancellation);
            var account = await Call(2, "account/read", new JsonObject { ["refreshToken"] = false });
            var response = await Call(3, "account/rateLimits/read", null);
            return Limits.OpenAI(identity.Key, response, JsonData.Text((account["account"] as JsonObject)?["email"]), identity.Secondary);
        }
        finally
        {
            try { process.StandardInput.Close(); } catch (IOException) { }
            if (!process.HasExited)
            {
                try { using var grace = new CancellationTokenSource(1000); await process.WaitForExitAsync(grace.Token); }
                catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } }
            }
            try { await errors.WaitAsync(TimeSpan.FromSeconds(1)); } catch (Exception) { }
        }
    }
    public void Dispose() { }
}

internal sealed class RemoteFailure(string message, DateTimeOffset? retry = null, bool authentication = false) : Exception(message)
{
    internal DateTimeOffset? Retry { get; } = retry;
    internal bool Authentication { get; } = authentication;
}

internal sealed class JsonTransport : IDisposable
{
    private readonly HttpClient _client;
    internal JsonTransport(HttpMessageHandler? handler = null) => _client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
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
                ?? DateTimeOffset.Now.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(2)));
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new RemoteFailure("Anmeldung erneuern", authentication: true);
        if (!response.IsSuccessStatusCode) throw new RemoteFailure($"Anbieter nicht erreichbar ({(int)response.StatusCode})");
        await response.Content.LoadIntoBufferAsync(2_000_000, cancellation);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellation)) as JsonObject
            ?? throw new InvalidDataException("Ungültige Anbieterantwort.");
    }
    public void Dispose() => _client.Dispose();
}

internal sealed class ClaudeSource : IAccountSource
{
    private readonly JsonTransport _transport = new();
    private DateTimeOffset _nextWallet, _nextRenew;
    private string? _wallet;
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var oauth = await CredentialAccess.Claude(identity.Profile, cancellation);
        var expires = JsonData.Number(oauth?["expiresAt"]);
        if (identity.AutoRenew && expires is > 0 && expires < DateTimeOffset.Now.AddMinutes(5).ToUnixTimeMilliseconds() && _nextRenew < DateTimeOffset.Now)
        {
            _nextRenew = DateTimeOffset.Now.AddMinutes(5);
            using var renewal = Process.Start(Platform.Cli("claude", identity.Profile, false, "mcp", "list"));
            if (renewal is not null)
            {
                var output = renewal.StandardOutput.ReadToEndAsync(cancellation);
                var error = renewal.StandardError.ReadToEndAsync(cancellation);
                try { await renewal.WaitForExitAsync(cancellation); }
                finally { if (!renewal.HasExited) renewal.Kill(true); }
                await Task.WhenAll(output, error);
            }
            oauth = await CredentialAccess.Claude(identity.Profile, cancellation);
        }
        var token = JsonData.Text(oauth?["accessToken"]);
        if (string.IsNullOrEmpty(token)) return new Reading(identity.Key, [], null, "Anmeldung erforderlich", null, true);
        var response = await _transport.Get("https://api.anthropic.com/api/oauth/usage", token, true, cancellation);
        JsonObject? account = null;
        var meta = Path.Combine(identity.Profile, ".claude.json");
        if (File.Exists(meta))
        {
            try { account = (JsonNode.Parse(File.ReadAllText(meta)) as JsonObject)?["oauthAccount"] as JsonObject; }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException) { }
        }
        if (identity.ShowBalance && _nextWallet < DateTimeOffset.Now && JsonData.Text(account?["organizationUuid"]) is string organization
            && Guid.TryParse(organization, out _))
        {
            _nextWallet = DateTimeOffset.Now.AddMinutes(5);
            try
            {
                var wallet = await _transport.Get("https://api.anthropic.com/api/oauth/organizations/" + organization + "/prepaid/credits", token, true, cancellation);
                _wallet = Limits.Wallet(wallet);
            }
            catch (RemoteFailure failure) { _wallet = null; _nextWallet = failure.Retry ?? _nextWallet; }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { _wallet = null; }
            catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException) { _wallet = null; }
        }
        return new Reading(identity.Key, Limits.Anthropic(response, identity.Secondary), identity.ShowBalance ? _wallet : null,
            JsonData.Text(account?["emailAddress"]) ?? "Verbunden", DateTimeOffset.Now);
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
