using System.Net;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class ApiChecks
{
    private static JsonObject Json(string json) => JsonNode.Parse(json)!.AsObject();
    internal static async Task Run(Action<bool, string> check)
    {
        var account = new Identity("api-example", "deepseek", "Beispiel", Path.GetTempPath(), true, true, 60, false);
        check(Providers.All.Select(item => item.Id).Distinct().Count() == Providers.All.Length, "Provider IDs unique");
        check(Providers.All.All(item => new Uri(item.Portal).Scheme == "https" && !string.IsNullOrWhiteSpace(item.Help)), "All providers have HTTPS portal and connection guidance");
        foreach (var invalid in new[] { "tiny", "example\ninvalid-key", "example key invalid", "nonascii-ä-example" })
        {
            var rejected = false; try { Providers.ValidateKey(invalid); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Invalid API key rejected: " + invalid.Length);
        }
        var deepseek = ApiAccountSource.ParseDeepSeek(account, Json("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"7.30\"},{\"currency\":\"USD\",\"total_balance\":\"0\"}]}"));
        check(deepseek.Balance == "0,00 USD" && deepseek.Detail!.Contains("7,30 CNY"), "DeepSeek preserves zero and independent currencies");
        var unavailable = ApiAccountSource.ParseDeepSeek(account, Json("{\"is_available\":false,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"1.20\"}]}"));
        check(unavailable.Failed && unavailable.Balance == "1,20 CNY", "DeepSeek availability is not inferred from balance");
        var missing = false; try { ApiAccountSource.ParseDeepSeek(account, Json("{\"balance_infos\":[]}")); } catch (InvalidDataException) { missing = true; }
        check(missing, "Missing DeepSeek balance is never zero");
        var moonshot = ApiAccountSource.ParseMoonshot(account with { Vendor = "moonshot" }, Json("{\"status\":true,\"data\":{\"available_balance\":2.5}}"));
        check(moonshot.Balance == "2,50 USD", "International Kimi balance uses USD");
        missing = false; try { ApiAccountSource.ParseMoonshot(account, Json("{\"status\":false,\"data\":{\"available_balance\":5}}")); } catch (InvalidDataException) { missing = true; }
        check(missing, "Failed Kimi payload never becomes a successful wallet");
        var router = ApiAccountSource.ParseOpenRouter(account, Json("{\"data\":{\"usage\":12.5,\"usage_monthly\":2.5,\"limit_remaining\":7.5}}"));
        check(router.Balance == "Rest 7,50 USD" && router.Detail!.Contains("kein Kontoguthaben"), "OpenRouter key budget is not account balance");
        check(ApiAccountSource.ParseOpenRouter(account, Json("{\"data\":{\"usage\":0,\"limit_remaining\":null}}")).Balance is null, "Unlimited key does not invent zero credit");
        var xai = ApiAccountSource.ParseXai(account, Json("{\"total\":{\"val\":\"-1000\"}}"));
        check(xai.Balance == "Saldo -10,00 USD", "xAI signed USD-cent balance is never silently inverted");
        check(!ReadingPresentation.Detail(account with { Vendor = "xai", ShowBalance = false }, xai).Contains("10,00") && !ReadingPresentation.Summary(account with { Vendor = "xai", ShowBalance = false }, xai).Contains("10,00"), "Hidden xAI balance is absent from summaries and tooltips");
        check(!ReadingPresentation.Detail(account with { ShowBalance = false }, deepseek).Contains("7,30"), "Hidden DeepSeek wallet is absent from details");
        var scoped = ApiAccountSource.EncodeCredential("xai", "example-test-key", "11111111-1111-1111-1111-111111111111");
        check(JsonData.Text(Json(scoped)["team"]) == "11111111-1111-1111-1111-111111111111", "xAI management credential requires an explicit team");
        var organization = ApiAccountSource.ParseOpenAi(account,
            JsonNode.Parse("[{\"results\":[{\"input_tokens\":1200,\"output_tokens\":300},{\"input_tokens\":20,\"output_tokens\":50}]}]")!.AsArray(),
            JsonNode.Parse("[{\"results\":[{\"amount\":{\"value\":1.25,\"currency\":\"usd\"}},{\"amount\":{\"value\":0.50,\"currency\":\"usd\"}}]}]")!.AsArray(), DateTimeOffset.UtcNow);
        check(organization.Balance is null && organization.Detail!.Contains("1,75 USD") && organization.Detail.Contains("1.220 Token") && organization.Detail.Contains("350 Token"), "Organization costs and text tokens summed without inventing credit");
        check(ApiAccountSource.ParseOpenAi(account, [], [], DateTimeOffset.UtcNow).Detail!.Contains("keine gemeldeten Kosten"), "Empty organization data does not invent zero USD");
        using (var portal = new ApiAccountSource((_, _, _) => throw new InvalidOperationException("No network expected"), (_, _) => throw new InvalidOperationException("No credential read expected")))
        {
            foreach (var provider in Providers.All.Where(item => item.Connection == ConnectionKind.Portal))
            {
                var reading = await portal.Query(account with { Vendor = provider.Id }, default);
                check(!reading.Failed && reading.Observed is null && reading.Balance is null && reading.Status.Contains("keine automatische"), provider.Id + ": portal neither fetches keys nor pretends connected");
            }
        }
        using (var absent = new ApiAccountSource(read: (_, _) => Task.FromResult<string?>(null)))
            check((await absent.Query(account, default)).Failed, "Missing API credential requests a key");
        var requested = new List<string>();
        using (var paginated = new ApiAccountSource((url, key, _) => {
            requested.Add(url); check(key == "example-test-key", "Credential passed only to API transport");
            if (url.Contains("usage/completions")) return Task.FromResult(url.Contains("&page=")
                ? Json("{\"data\":[{\"results\":[{\"input_tokens\":30,\"output_tokens\":4}]}],\"has_more\":false}")
                : Json("{\"data\":[{\"results\":[{\"input_tokens\":10,\"output_tokens\":2}]}],\"has_more\":true,\"next_page\":\"cursor & example\"}"));
            return Task.FromResult(Json("{\"data\":[],\"has_more\":false}"));
        }, (_, _) => Task.FromResult<string?>("example-test-key")))
        {
            var result = await paginated.Query(account with { Vendor = "openai-api" }, default);
            check(requested.Count == 3 && requested[1].Contains("cursor%20%26%20example") && result.Detail!.Contains("40 Token"), "Every usage page included with escaped cursor");
            check(requested.All(url => new Uri(url).Host == "api.openai.com" && !url.Contains("example-test-key")), "API key never included in URL");
        }
        foreach (var payload in new[] { "{\"data\":[]}", "{\"data\":[],\"has_more\":\"false\"}", "{\"data\":[],\"has_more\":true,\"next_page\":\"repeated\"}" })
        {
            using var malformed = new ApiAccountSource((_, _, _) => Task.FromResult(Json(payload)), (_, _) => Task.FromResult<string?>("example-test-key"));
            var rejected = false;
            try { await malformed.Query(account with { Vendor = "openai-api" }, default); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "Incomplete or cyclic cost pagination never succeeds: " + payload.Length);
        }
        using (var transport = new JsonTransport(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Sensitive provider response") })))
        {
            var safe = false; try { await transport.Get("https://api.deepseek.com/user/balance", "example-test-key", false, default); }
            catch (RemoteFailure error) { safe = error.Authentication && !error.Message.Contains("Sensitive"); }
            check(safe, "Provider error bodies and credentials never echoed");
            var https = false; try { await transport.Get("http://example.invalid", "example-test-key", false, default); } catch (InvalidOperationException) { https = true; }
            check(https, "Credentials never sent over HTTP");
        }
        using (var transport = new JsonTransport(new FakeHandler(request => {
            check(request.Method == HttpMethod.Get && request.Headers.Authorization?.Scheme == "Bearer", "Read-only authenticated billing request");
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new(TimeSpan.FromMinutes(3)); return response;
        })))
        {
            var delayed = false; try { await transport.Get("https://api.deepseek.com/user/balance", "example-test-key", false, default); }
            catch (RemoteFailure error) { delayed = error.Retry > DateTimeOffset.Now.AddMinutes(2); }
            check(delayed, "API provider retry-after retained");
        }
    }
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
