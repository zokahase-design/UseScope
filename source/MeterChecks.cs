using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class MeterChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        static UsageCapture Capture(string json, string vendor = "custom-api", bool sse = false)
        {
            var capture = new UsageCapture(vendor, sse); var bytes = Encoding.UTF8.GetBytes(json);
            for (var offset = 0; offset < bytes.Length; offset += 3) capture.Feed(bytes.AsSpan(offset, Math.Min(3, bytes.Length - offset)));
            capture.Finish(); return capture;
        }
        var plain = Capture("{\"model\":\"example\",\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":23,\"total_tokens\":123}}");
        check(plain.Input == 100 && plain.Output == 23 && plain.Total == 123 && plain.Cost is null, "Local meter counts reported tokens without pricing guesses");
        var sse = "data: {\"id\":\"fixture-response\",\"model\":\"Mödel\",\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}\r\n\r\n";
        var streamed = Capture(sse + sse + "data: [DONE]\n\n", sse: true);
        check(streamed.Total == 15 && streamed.Model is null, "Fragmented UTF-8 SSE counts usage once and rejects non-identifier model metadata");
        check(Capture("{\"model\":\"private response text must not be journaled\",\"usage\":{\"total_tokens\":5}}").Model is null,
            "Provider model metadata cannot persist free-form response text into the journal");
        check(Capture("data: {\"usage\":\ndata: {\"input_tokens\":3,\"output_tokens\":2}}\n\n", sse: true).Total == 5, "Multiline SSE data is joined as one event");
        var missing = Capture("data: {\"choices\":[{\"delta\":{\"content\":\"Example\"}}]}\n\ndata: [DONE]\n\n", sse: true);
        check(missing.Total is null && missing.Input is null, "Streaming without usage stays unknown instead of zero");
        var anthropic = Capture("data: {\"message\":{\"id\":\"example-message\",\"usage\":{\"input_tokens\":10,\"cache_read_input_tokens\":20,\"cache_creation_input_tokens\":5,\"output_tokens\":0}}}\n\ndata: {\"usage\":{\"output_tokens\":7}}\n\n", sse: true);
        check(anthropic.Input == 35 && anthropic.Output == 7 && anthropic.Total == 42, "Anthropic cache input plus cumulative output counts once for custom endpoints too");
        var google = Capture("{\"usageMetadata\":{\"promptTokenCount\":10,\"candidatesTokenCount\":3,\"thoughtsTokenCount\":5,\"totalTokenCount\":18}}");
        check(google.Input == 10 && google.Output == 8 && google.Total == 18, "Gemini includes reported thinking tokens without duplicate totals");
        var responses = Capture("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"example-response\",\"usage\":{\"input_tokens\":11,\"output_tokens\":9,\"total_tokens\":20}}}\n\n", sse: true);
        check(responses.Total == 20, "Responses API final nested usage supported");
        check(Capture("{\"usage\":{\"total_tokens\":-1}}").Total is null, "Malformed negative usage does not become a measurement");
        check(Capture("{\"usage\":{\"cost\":0.002,\"total_tokens\":2}}", "openrouter").Currency == "USD", "Only documented OpenRouter response cost is labeled USD");
        check(Capture("{\"usage\":{\"cost\":0.002,\"total_tokens\":2}}").Cost is null, "Unknown monetary units are not inferred");
        check(Capture("{\"usage\":{\"cost_in_usd_ticks\":25000000}}", "xai").Cost == 0.0025m, "xAI reported USD ticks converted at documented 1e10 scale");
        check(Capture("data: {\"type\":\"message-end\",\"delta\":{\"usage\":{\"tokens\":{\"input_tokens\":8,\"output_tokens\":4},\"billed_units\":{\"input_tokens\":2,\"output_tokens\":4}}}}\n\n", "cohere", true).Total == 12, "Cohere native final event uses actual tokens without mixing billed units");
        using (var headers = new HttpResponseMessage(HttpStatusCode.OK)) {
            headers.Headers.Add("x-ratelimit-limit-tokens-daily", "10000000"); headers.Headers.Add("x-ratelimit-remaining-tokens-daily", "9000000");
            var quota = LoopbackMeterServer.DailyQuota(headers, DateTimeOffset.Parse("2026-11-03T12:00:00Z"));
            check(quota is { Limit: 10000000, Remaining: 9000000 } && quota.Reset == DateTimeOffset.Parse("2026-11-04T00:00:00Z"), "IFM daily quota uses reported headers and documented UTC midnight reset");
        }
        foreach (var path in new[] { "v1/../admin/chat/completions", "v1/%2e%2e/admin/chat/completions", "v1/%252e%252e/admin/chat/completions", "v1%2fchat/completions", "v1\\chat/completions", "v1//chat/completions" })
        { var rejected = false; try { MeterNetwork.RelativeRoute(path); } catch (ArgumentException) { rejected = true; } check(rejected, "Encoded or ambiguous path rejected: " + path); }
        check(!MeterNetwork.InferencePath("POST", "/admin/chat/completions") && !MeterNetwork.InferencePath("GET", "/admin/models"), "Inference allowlist matches full relative routes, never suffixes");
        foreach (var address in new[] { "http://api.example.com/v1", "https://127.0.0.1/v1", "https://[::1]/v1", "https://10.1.2.3/v1", "https://169.254.169.254", "https://localhost/", "https://service.local/", "https://user:secret@api.example.com/", "https://api.example.com/?key=secret" })
        {
            var rejected = false; try { MeterNetwork.ValidateBase(address); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Unsafe upstream URL rejected: " + address.Replace("secret", "example"));
        }
        check(!MeterNetwork.Public(IPAddress.Parse("::ffff:192.168.1.1")) && !MeterNetwork.Public(IPAddress.Parse("100.64.1.1")) && MeterNetwork.Public(IPAddress.Parse("8.8.8.8")), "DNS policy rejects mapped private and shared addresses");
        var values = new Dictionary<string, string> { ["key"] = "fixture-inference-secret", ["upstream"] = "https://api.example.com/v1", ["auth"] = "bearer" };
        var credential = MeterSetup.Encode(values); var another = MeterSetup.Encode(values);
        check(credential.LocalToken != another.LocalToken && credential.LocalToken.Length >= 64 && credential.LocalToken != credential.Key, "Local client tokens are random and separate from provider keys");
        var temporary = Path.Combine(Path.GetTempPath(), "usescope-meter-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var account = new Identity("fictional-meter", "custom-api", "Example", temporary, true, true, 60, false, ConnectionOptions: MeterSetup.Options());
            check(MeterStorage.SecretIdentity(account).Key != account.Key && MeterStorage.SecretIdentity(account).Profile != account.Profile, "Meter keys use a separate credential slot from admin keys");
            var ledger = new MeterLedger(); var calls = 0; var mode = "normal";
            var body = "{\"model\":\"fixture-model\",\"id\":\"same-idempotent-response\",\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15},\"choices\":[{\"message\":{\"content\":\"private-answer-text\"}}]}";
            var handler = new Handler(async (request, token) => {
                calls++;
                check(request.RequestUri!.Scheme == "https" && request.RequestUri.Host == "api.example.com" && request.RequestUri.AbsolutePath == "/v1/chat/completions", "Meter forwards only the configured HTTPS inference target");
                check(request.Headers.Authorization?.Parameter == credential.Key && !request.Headers.ToString().Contains(credential.LocalToken), "Local key replaced by upstream inference key");
                check(!request.Headers.Contains("Cookie") && !request.Headers.Contains("X-Forwarded-Host") && !request.RequestUri.Query.Contains("key", StringComparison.OrdinalIgnoreCase), "Client secrets and arbitrary headers stripped");
                check(await request.Content!.ReadAsStringAsync(token) == "{\"prompt\":\"private-prompt-text\"}", "Request body forwarded without modifying prompts");
                if (mode == "redirect") return new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://other.example.com/") }, Content = new StringContent("redirect") };
                if (mode == "cancel") { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(mode == "stream" ? sse + "data: [DONE]\n\n" : body, Encoding.UTF8, mode == "stream" ? "text/event-stream" : "application/json") };
            });
            await using var server = new LoopbackMeterServer(() => [account], ledger, handler, (_, _) => Task.FromResult<MeterCredential?>(credential));
            await server.Start(0); using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            var url = LoopbackMeterServer.ClientUrl(account, server.Port);
            HttpRequestMessage Request(string suffix = "/chat/completions", string? key = null) {
                var message = new HttpRequestMessage(HttpMethod.Post, url + suffix) { Content = new StringContent("{\"prompt\":\"private-prompt-text\"}", Encoding.UTF8, "application/json") };
                message.Headers.Authorization = new("Bearer", key ?? credential.LocalToken); message.Headers.Add("Cookie", "private-cookie"); message.Headers.Add("X-Forwarded-Host", "evil.example"); return message;
            }
            using (var request = Request(key: "wrong-local-token")) using (var response = await client.SendAsync(request)) check(response.StatusCode == HttpStatusCode.Unauthorized && calls == 0, "Wrong local token cannot send a paid request");
            using (var request = Request()) { request.Headers.Host = "evil.example"; using var response = await client.SendAsync(request); check(response.StatusCode == HttpStatusCode.Forbidden && calls == 0, "DNS-rebinding Host header blocked"); }
            using (var request = Request("/admin/keys")) using (var response = await client.SendAsync(request)) check(response.StatusCode == HttpStatusCode.BadRequest && calls == 0, "Administrative API operations blocked before metering");
            using (var request = Request("/chat/completions?KEY=client-secret&api_key=client-secret&token=client-secret&apiKey=client-secret&client_secret=client-secret")) using (var response = await client.SendAsync(request)) {
                check(response.StatusCode == HttpStatusCode.OK && await response.Content.ReadAsStringAsync() == body, "Non-streaming response passed through unchanged");
                check(!response.Headers.Contains("Access-Control-Allow-Origin"), "Local meter exposes no browser CORS permission");
            }
            using (var request = Request()) using (var response = await client.SendAsync(request)) await response.Content.ReadAsStringAsync();
            mode = "stream";
            using (var request = Request()) using (var response = await client.SendAsync(request)) check(await response.Content.ReadAsStringAsync() == sse + "data: [DONE]\n\n", "SSE response passed through unchanged");
            mode = "redirect";
            using (var request = Request()) using (var response = await client.SendAsync(request)) check(response.StatusCode == HttpStatusCode.BadGateway, "Upstream redirects are blocked");
            MeterSummary summary = await ledger.Read(account, DateTimeOffset.UtcNow.AddMinutes(-5), default);
            for (var retry = 0; retry < 40 && summary.Requests < 4; retry++) { await Task.Delay(25); summary = await ledger.Read(account, DateTimeOffset.UtcNow.AddMinutes(-5), default); }
            check(summary.Requests == 4 && summary.Successful == 3 && summary.Total == 30, "Routed attempts counted, idempotent duplicate response tokens not charged twice");
            var disk = await File.ReadAllTextAsync(Path.Combine(temporary, "meter", "usage.jsonl"));
            check(!disk.Contains("private-prompt-text") && !disk.Contains("private-answer-text") && !disk.Contains(credential.LocalToken) && !disk.Contains(credential.Key) && !disk.Contains("private-cookie") && !disk.Contains("fixture-model"), "Meter ledger contains no prompts, responses, cookies, keys or provider model strings");
            check(summary.UnknownTokenRequests == 1, "Unmeasured redirect attempt remains unknown");
            mode = "cancel"; var cancelled = false;
            using (var request = Request()) using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(500)))
                try { using var response = await client.SendAsync(request, timeout.Token); } catch (OperationCanceledException) { cancelled = true; }
            check(cancelled, "Client can cancel an in-flight inference without waiting for server timeout");
            var other = account with { Key = "other-account", Profile = Path.Combine(temporary, "other") };
            check((await ledger.Read(other, DateTimeOffset.UtcNow.AddDays(-1), default)).Requests == 0, "Different accounts never share meter totals");
            await ledger.Append(other, new("partial", DateTimeOffset.UtcNow, 200, 10, 0, 10, "fixture", null, null, false, "fixture-fingerprint"), default);
            await ledger.Append(other, new("retry", DateTimeOffset.UtcNow, 200, 10, 5, 15, "fixture", 0.01m, "USD", true, "fixture-fingerprint"), default);
            await ledger.Append(other, new("repeat", DateTimeOffset.UtcNow, 200, 10, 5, 15, "fixture", 0.01m, "USD", true, "fixture-fingerprint"), default);
            var recovered = await ledger.Read(other, DateTimeOffset.UtcNow.AddDays(-1), default);
            check(recovered.Requests == 3 && recovered.Input == 10 && recovered.Output == 5 && recovered.Total == 15 && recovered.Cost == 0.01m, "Retry after partial stream completes usage once without losing final tokens or cost");
            check(ProviderCatalog.Rows.Length == 53 && ProviderCatalog.Rows.All(row => Providers.Known(row.Id)), "All 53 requested provider families mapped to explicit capabilities");
            foreach (var row in ProviderCatalog.Rows.Where(row => row.Base.Length > 0)) check(MeterNetwork.ValidateBase(row.Base).Scheme == "https", row.Name + ": configured meter base is public HTTPS");
            foreach (var kind in new[] { "gemini", "cohere" })
            {
                var native = account with { Key = "native-" + kind, Vendor = kind, Profile = Path.Combine(temporary, kind) };
                var nativeCredential = credential with { Upstream = "https://api.example.com/" + (kind == "gemini" ? "v1beta/" : "v2/"), Auth = kind == "gemini" ? "x-goog-api-key" : "bearer" };
                var nativeCalls = 0;
                await using var nativeServer = new LoopbackMeterServer(() => [native], ledger, new Handler((request, _) => {
                    nativeCalls++; var expectedPath = kind == "gemini" ? "/v1beta/models/fixture:generateContent" : "/v2/chat";
                    check(request.RequestUri!.AbsolutePath == expectedPath, kind + ": native client path keeps exactly one API version");
                    check(kind != "gemini" || request.Headers.GetValues("x-goog-api-key").Single() == credential.Key && request.RequestUri.Query == "?alt=sse", "Google key stays in header while SSE query is preserved");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}", Encoding.UTF8, "application/json") });
                }), (_, _) => Task.FromResult<MeterCredential?>(nativeCredential));
                await nativeServer.Start(0);
                var nativeUrl = LoopbackMeterServer.ClientUrl(native, nativeServer.Port)[..^3] + (kind == "gemini" ? "/v1beta/models/fixture:generateContent?alt=sse&key=discard" : "/v2/chat");
                using var nativeRequest = new HttpRequestMessage(HttpMethod.Post, nativeUrl) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                nativeRequest.Headers.Authorization = new("Bearer", credential.LocalToken);
                using var nativeResponse = await client.SendAsync(nativeRequest); await nativeResponse.Content.ReadAsStringAsync();
                check(nativeResponse.IsSuccessStatusCode && nativeCalls == 1, kind + ": native local API forwarding works without paid network calls");
            }
            var arcee = ArceeSource.Parse(account, JsonNode.Parse("{\"total_input_tokens\":10,\"total_output_tokens\":5,\"total_tokens\":15,\"total_requests\":2}")!.AsObject(), DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
            check(arcee.Detail!.Contains("15 Token") && arcee.Balance is null, "Arcee account tokens never imply money");
            var step = StepFunSource.Parse(account, JsonNode.Parse("{\"object\":\"account\",\"type\":\"prepaid\",\"balance\":\"2.50\",\"total_cash_balance\":\"1000\"}")!.AsObject());
            check(step.Balance == "Saldo 2,50 USD*" && !step.Balance.Contains("1000"), "StepFun current balance excludes lifetime deposits");
            var ocean = DigitalOceanBillingSource.Parse(account, JsonNode.Parse("{\"account_balance\":\"-1.25\",\"month_to_date_usage\":\"2.50\",\"month_to_date_balance\":\"1.25\",\"generated_at\":\"2026-11-03T00:00:00Z\"}")!.AsObject());
            check(ocean.Balance == "Saldo 1,25 USD*" && ocean.Detail!.Contains("Cloudkonto") && ocean.Detail.Contains("-1,25 USD"), "DigitalOcean cloud balance preserves signed values and full-account billing scope");
            var sawMistral = false;
            using var mistral = new MistralAdminSource((request, _) => {
                sawMistral = request.Headers.Contains("x-api-key") && !request.Headers.Contains("Authorization");
                return Task.FromResult(JsonNode.Parse("{\"groups\":[],\"aggregates\":{\"total_api_calls\":4},\"metadata\":{\"has_more\":false}}")!.AsObject());
            }, (_, _) => Task.FromResult<string?>("fixture-admin-key"));
            await mistral.Query(account, default); check(sawMistral, "Mistral enterprise queries use the documented x-api-key admin header");
        }
        finally
        {
            // Exact test-created root, never a provider profile.
            var absolute = Path.GetFullPath(temporary);
            if (!Path.GetFileName(absolute).StartsWith("usescope-meter-check-", StringComparison.Ordinal) || Path.GetDirectoryName(absolute) != Path.TrimEndingDirectorySeparator(Path.GetTempPath())) throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(absolute, true);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) => send(request, cancellation); }
}
