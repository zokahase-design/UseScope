using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace UseScope.Desktop;

internal sealed class LoopbackMeterServer : IAsyncDisposable
{
    internal const int DefaultPort = 48371;
    private readonly Func<IReadOnlyList<Identity>> _accounts;
    private readonly Func<Identity, CancellationToken, Task<MeterCredential?>> _read;
    private readonly MeterLedger _ledger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _requests = new(16, 16);
    private readonly CancellationTokenSource _lifetime = new();
    private WebApplication? _app;
    internal int Port { get; private set; }
    internal LoopbackMeterServer(Func<IReadOnlyList<Identity>> accounts, MeterLedger ledger, HttpMessageHandler? handler = null,
        Func<Identity, CancellationToken, Task<MeterCredential?>>? read = null)
    {
        _accounts = accounts; _ledger = ledger; _read = read ?? MeterStorage.ReadCredential;
        _http = new HttpClient(handler ?? MeterNetwork.Handler()) { Timeout = Timeout.InfiniteTimeSpan };
    }
    internal static string ClientUrl(Identity account, int port = DefaultPort) => "http://127.0.0.1:" + port + "/api/" + Uri.EscapeDataString(account.Key) + "/v1";
    internal async Task Start(int port = DefaultPort, CancellationToken cancellation = default)
    {
        if (port is < 0 or > 65535 || port is > 0 and < 1024) throw new ArgumentOutOfRangeException(nameof(port));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production", ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
        builder.WebHost.UseSetting(WebHostDefaults.PreventHostingStartupKey, "true");
        builder.WebHost.ConfigureKestrel(options => {
            options.AddServerHeader = false; options.Listen(IPAddress.Loopback, port);
            options.Limits.MaxRequestBodySize = 64 * 1024 * 1024; options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        });
        var app = builder.Build(); app.Run(Forward);
        try { await app.StartAsync(cancellation); _app = app; Port = new Uri(app.Urls.Single()).Port; }
        catch { await app.DisposeAsync(); throw; }
    }
    private static bool Equal(string? supplied, string expected)
    {
        if (supplied is null || supplied.Length > 4096) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }
    private static string? Token(HttpContext context)
    {
        var auth = context.Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return auth[7..];
        foreach (var name in new[] { "x-api-key", "api-key", "x-goog-api-key", "api-subscription-key" })
            if (context.Request.Headers.TryGetValue(name, out var value)) return value.ToString();
        return null;
    }
    private static async Task Error(HttpContext context, int status, string code)
    {
        if (context.Response.HasStarted) { context.Abort(); return; }
        context.Response.StatusCode = status; context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":{\"message\":\"UseScope: " + code + "\",\"type\":\"usescope_meter_error\"}}", context.RequestAborted);
    }
    private async Task Forward(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is not IPAddress remote || !IPAddress.IsLoopback(remote)
            || context.Request.Host.Host != "127.0.0.1") { await Error(context, 403, "loopback_only"); return; }
        var segments = (context.Request.Path.Value ?? "").Split('/', 4);
        if (segments.Length != 4 || segments[1] != "api") { await Error(context, 404, "unknown_route"); return; }
        var account = _accounts().FirstOrDefault(value => value.Enabled && MeterStorage.Enabled(value) && value.Key == segments[2]);
        if (account is null) { await Error(context, 404, "unknown_account"); return; }
        MeterCredential? credential;
        try { credential = await _read(account, context.RequestAborted); }
        catch (Exception) { await Error(context, 503, "credential_store_unavailable"); return; }
        if (credential is null || credential.LocalToken.Length < 32 || !Equal(Token(context), credential.LocalToken)) { await Error(context, 401, "local_key_required"); return; }
        if (!await _requests.WaitAsync(0, context.RequestAborted)) { context.Response.Headers.RetryAfter = "1"; await Error(context, 429, "too_many_requests"); return; }
        var status = 502; var completed = false; var capture = new UsageCapture(account.Vendor, false); var count = false; MeterQuota? quota = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _lifetime.Token); cancellation.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            var root = MeterNetwork.ValidateBase(credential.Upstream); var relative = segments[3];
            relative = MeterNetwork.RelativeRoute(relative);
            if (relative.StartsWith("v1/", StringComparison.Ordinal)) relative = relative[3..];
            else { var version = relative.Split('/')[0]; if (relative.StartsWith(version + "/", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(version, "^v[1-9][0-9]?(beta[0-9]*)?$") && root.AbsolutePath.EndsWith("/" + version + "/", StringComparison.Ordinal)) relative = relative[(version.Length + 1)..]; }
            // Only documented API-version/stream-format query parameters pass; credentials use headers only.
            var query = context.Request.Query.Where(item => new[] { "api-version", "alt" }.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
                .SelectMany(item => item.Value.Select(value => Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(value ?? "")));
            var target = new Uri(root.AbsoluteUri + relative.TrimStart('/') + (query.Any() ? "?" + string.Join("&", query) : ""));
            if (target.Scheme != root.Scheme || target.Authority != root.Authority || !target.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)
                || !MeterNetwork.InferencePath(context.Request.Method, "/" + relative)) { await Error(context, 400, "unsupported_inference_route"); return; }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
            if (context.Request.Method == "POST")
            {
                request.Content = new StreamContent(context.Request.Body);
                if (context.Request.ContentType is string contentType) request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                if (context.Request.ContentLength is long length) request.Content.Headers.ContentLength = length;
            }
            if (credential.Auth == "bearer") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Key);
            else if (credential.Auth is "x-api-key" or "api-key" or "x-goog-api-key" or "api-subscription-key") request.Headers.Add(credential.Auth, credential.Key);
            else throw new InvalidDataException("Unbekanntes Auth-Verfahren.");
            foreach (var name in new[] { "anthropic-version", "anthropic-beta", "OpenAI-Organization", "OpenAI-Project", "Idempotency-Key" })
                if (context.Request.Headers.TryGetValue(name, out var value)) request.Headers.TryAddWithoutValidation(name, value.ToArray());
            if ((account.Vendor == "anthropic-api" || root.Host == "api.anthropic.com") && !request.Headers.Contains("anthropic-version")) request.Headers.Add("anthropic-version", "2023-06-01");
            count = context.Request.Method == "POST";
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token); status = (int)response.StatusCode;
            if (account.Vendor == "ifm" && root.Host == "api.ifm.ai") quota = DailyQuota(response, DateTimeOffset.UtcNow);
            if (status is >= 300 and < 400) { await Error(context, 502, "upstream_redirect_blocked"); return; }
            context.Response.StatusCode = status;
            if (response.Content.Headers.ContentType is { } type) context.Response.ContentType = type.ToString();
            foreach (var header in response.Headers)
                if (header.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase) || header.Key.Equals("x-request-id", StringComparison.OrdinalIgnoreCase)
                    || header.Key.StartsWith("x-ratelimit-", StringComparison.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
            capture = new UsageCapture(account.Vendor, response.Content.Headers.ContentType?.MediaType == "text/event-stream");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token); var bytes = new byte[32 * 1024]; int read;
            while ((read = await stream.ReadAsync(bytes, cancellation.Token)) > 0)
            {
                capture.Feed(bytes.AsSpan(0, read)); await context.Response.Body.WriteAsync(bytes.AsMemory(0, read), cancellation.Token);
                await context.Response.Body.FlushAsync(cancellation.Token);
            }
            capture.Finish(); completed = true;
        }
        catch (OperationCanceledException) { if (!context.RequestAborted.IsCancellationRequested) await Error(context, 504, "request_cancelled"); }
        catch (Exception) { await Error(context, 502, "upstream_or_configuration_error"); }
        finally
        {
            _requests.Release();
            if (count)
            {
                try {
                    // No request/response text, headers or secrets enter the ledger.
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var fingerprint = capture.ResponseId is string responseId ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential.Upstream + "\n" + responseId))) : null;
                    await _ledger.Append(account, new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, status, capture.Input, capture.Output, capture.Total, null, capture.Cost, capture.Currency, completed, fingerprint, quota), timeout.Token);
                } catch (Exception) { _ledger.RecordingFailed(account); /* A successful inference must never trigger a retry because local recording failed. */ }
            }
        }
    }
    internal static MeterQuota? DailyQuota(HttpResponseMessage response, DateTimeOffset now)
    {
        long? Header(string name) { var values = response.Headers.TryGetValues(name, out var entries) ? entries.Take(2).ToArray() : []; return values.Length == 1 && long.TryParse(values[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null; }
        var limit = Header("x-ratelimit-limit-tokens-daily"); var remaining = Header("x-ratelimit-remaining-tokens-daily");
        return limit is > 0 && remaining is >= 0 && remaining <= limit ? new(limit.Value, remaining.Value, new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero)) : null;
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel(); if (_app is not null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); try { await _app.StopAsync(timeout.Token); } finally { await _app.DisposeAsync(); } }
        _http.Dispose(); _lifetime.Dispose();
    }
}
