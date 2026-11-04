using System.Diagnostics;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class BroadAdapterChecks
{
    private static JsonObject Json(string value) => JsonNode.Parse(value)!.AsObject();
    internal static async Task Run(Action<bool, string> check)
    {
        var account = new Identity("fixture-broad", "anthropic-api", "Example", Path.Combine(Path.GetTempPath(), "usescope-fictional-profile"), true, true, 120, false);
        var snapshot = DateTimeOffset.Parse("2026-11-03T00:00:00Z");
        var usage = JsonNode.Parse("[{\"usage\":{\"uncached_input_tokens\":20,\"cache_read_input_tokens\":30,\"cache_creation\":{\"ephemeral_1h_input_tokens\":5,\"ephemeral_5m_input_tokens\":5},\"output_tokens\":40,\"total_tokens\":100}}]")!.AsArray();
        var cost = JsonNode.Parse("[{\"cost\":{\"currency\":\"USD\",\"amount\":\"250.123456\"}}]")!.AsArray();
        var report = AnthropicAnalyticsSource.Parse(account, usage, cost, snapshot.AddDays(-2), snapshot, snapshot, snapshot);
        check(report.Balance is null && report.Detail!.Contains("2,50 USD") && report.Detail.Contains("100 Token") && report.Detail.Contains("Input inkl. Cache: 60"), "Anthropic analytics cents and cache tokens counted once");
        check(AnthropicAnalyticsSource.Parse(account, [], [], snapshot, snapshot, null, null).Observed is null, "Empty Anthropic analytics never invents freshness");
        var invalid = false;
        try { AnthropicAnalyticsSource.Parse(account, usage, cost, snapshot, snapshot, null, snapshot); } catch (InvalidDataException) { invalid = true; }
        check(invalid, "Anthropic rows without snapshot rejected");
        var calls = 0;
        using (var source = new AnthropicAnalyticsSource((request, _) => {
            calls++; check(request.Method == HttpMethod.Get && request.Headers.Contains("x-api-key") && !request.Headers.Contains("Authorization"), "Anthropic uses read-only admin authentication");
            check(request.Headers.GetValues("anthropic-beta").Single() == "analytics-usage-cost-2026-08-24", "Current Anthropic analytics beta requested");
            var page = new JsonObject { ["data"] = new JsonArray(), ["has_more"] = false, ["data_refreshed_at"] = snapshot.ToString("O") };
            if (request.RequestUri!.AbsolutePath.EndsWith("usage")) page["data"] = usage.DeepClone(); else page["data"] = cost.DeepClone();
            request.Dispose(); return Task.FromResult(page);
        }, (_, _) => Task.FromResult<string?>("example-admin-key")))
        {
            var result = await source.Query(account, default);
            if (!(DateTimeOffset.UtcNow.Day == 1 && DateTimeOffset.UtcNow.Hour == 0)) check(calls == 2 && result.Detail!.Contains("2,50 USD"), "Both Anthropic organization reports fetched");
        }
        var wallet = Json("{\"Success\":true,\"Data\":{\"Currency\":\"USD\",\"AvailableCashAmount\":\"12.34\",\"AvailableAmount\":\"999.99\"}}");
        var bill = Json("{\"Success\":true,\"Data\":{\"BillingCycle\":\"2026-11\",\"Items\":{\"Item\":[{\"ProductCode\":\"example\",\"Currency\":\"USD\",\"PretaxAmount\":\"2.10\"}]}}}");
        var ali = AlibabaBillingSource.Parse(account with { Vendor = "qwen" }, wallet, bill, "2026-11", "example");
        check(ali.Balance == "12,34 USD" && ali.Detail!.Contains("2,10 USD") && !ali.Detail.Contains("999,99"), "Alibaba cash excludes credit and bills keep scope");
        check(AlibabaBillingSource.Parse(account, wallet, Json("{\"Success\":true,\"Data\":{\"BillingCycle\":\"2026-11\"}}"), "2026-11", "").Balance == "12,34 USD", "Alibaba empty bill does not discard a valid wallet");
        check(AlibabaBillingSource.Parse(account, wallet, Json("{\"Success\":false}"), "2026-11", "").Status.Contains("Kostenrechte fehlen"), "Alibaba missing bill rights do not invent zero spend");
        using (var signed = AlibabaBillingSource.Sign("business.ap-southeast-1.aliyuncs.com", "QueryAccountBalance", [], "example-access-id", "example-secret-value", snapshot, "example-nonce"))
        {
            check(signed.Method == HttpMethod.Get && signed.Headers.GetValues("Authorization").Single().StartsWith("ACS3-HMAC-SHA256 Credential=example-access-id,"), "Alibaba signed read-only API action");
            check(!signed.RequestUri!.ToString().Contains("example-secret") && !signed.RequestUri.ToString().Contains("example-access"), "Alibaba secrets never in URL");
        }
        var coding = PlanSource.Alibaba(account with { Vendor = "alibaba-coding" }, Json("{\"per5Hour\":{\"percentage\":0.38,\"resetTime\":1900000000000},\"perWeek\":{\"percentage\":0.5,\"resetTime\":1900000000000}}"));
        check(coding.Periods.Count == 2 && coding.Periods[0].Percent == 38, "Bailian coding fractions are consumed percentages");
        var tokenPlan = PlanSource.Alibaba(account with { Vendor = "alibaba-token" }, Json("{\"hasQuotaData\":true,\"per3DayPercentage\":0.25,\"per3DayResetTime\":1900000000000}"));
        check(tokenPlan.Periods.Single().Percent == 25 && tokenPlan.Periods[0].Caption == "3d", "Bailian token windows support documented dynamic intervals");
        check(PlanSource.Alibaba(account with { Vendor = "alibaba-token" }, Json("{\"hasQuotaData\":false}")).Failed, "Missing token plan is not unlimited quota");
        var mini = PlanSource.MiniMax(account with { Vendor = "minimax" }, Json("{\"base_resp\":{\"status_code\":0},\"model_remains\":[{\"model_name\":\"general\",\"current_interval_total_count\":0,\"current_interval_usage_count\":0,\"current_interval_remaining_percent\":94,\"current_weekly_remaining_percent\":98}]}"));
        check(mini.Periods.Count == 2 && mini.Periods[0].Percent == 6 && mini.Periods[1].Percent == 2, "MiniMax uses authoritative remaining percent despite zero counts");
        check(PlanSource.MiniMax(account, Json("{\"model_remains\":[{\"model_name\":\"video\",\"current_interval_total_count\":0,\"current_interval_status\":3,\"current_interval_remaining_percent\":100}]}")).Periods.Count == 0, "MiniMax status 3 plus zero quota never claims free video access");
        var miniCash = MiniMaxBalanceSource.Parse(account, Json("{\"base_resp\":{\"status_code\":0},\"available_amount\":\"98.00\"}"), "USD");
        check(miniCash.Balance == "98,00 USD*" && miniCash.Detail!.Contains("vom Nutzer eingestellt"), "MiniMax unreported currency remains explicitly configured");
        var muse = MuseSource.Parse(account with { Vendor = "meta" }, Json("{\"usage\":{\"tier\":\"Example\",\"observedAtMs\":1793664000000,\"window\":{\"usedPercent\":31,\"resetsAtMs\":1900000000000,\"windowDurationMins\":300},\"weekly\":{\"usedPercent\":42,\"resetsAtMs\":1900000000000}}}"), false);
        check(muse.Periods.Count == 2 && muse.Periods[0].Percent == 31 && muse.Status.Contains("letzter gemeldeter Stand"), "Muse stable read preserves provider observation and stale-read scope");
        check(MuseSource.Parse(account, Json("{\"subscriptionActive\":false}"), true).Status.Contains("kein aktives Abo"), "Muse inactive subscription is distinguished from empty usage");
        var google = GoogleProjectSource.ParsePoints(JsonNode.Parse("[{\"value\":{\"int64Value\":\"20\"},\"interval\":{\"endTime\":\"2026-11-02T12:00:00Z\"}},{\"value\":{\"int64Value\":\"30\"},\"interval\":{\"endTime\":\"2026-11-02T13:00:00Z\"}}]")!.AsArray());
        check(google.Count == 50 && google.Observed!.Value.Hour == 13 && GoogleProjectSource.ParsePoints([]).Count is null, "Google int64 DELTA points summed with real interval timestamps");
        var query = GoogleProjectSource.BillingQuery("example-project", "export-project.billing.usage_table", snapshot.AddDays(-2), snapshot);
        check(JsonData.Text(query["query"])!.StartsWith("SELECT ") && JsonData.Text(query["query"])!.Contains("project.id = @project") && JsonData.Text(query["maximumBytesBilled"]) == "104857600", "Google billing query is parameterized read-only with billing cap");
        invalid = false; try { ConnectionSetup.Google("example-project", "export-project.dataset.table`;DELETE"); } catch (ArgumentException) { invalid = true; }
        check(invalid, "Billing export table cannot inject SQL");
        foreach (var vendor in new[] { "codex", "claude", "nous", "meta", "minimax", "gemini", "alibaba-coding", "alibaba-token" })
        {
            var process = new ProcessStartInfo(); process.Environment["CLOUDSDK_AUTH_ACCESS_TOKEN"] = "example-ambient-token"; process.Environment["DASHSCOPE_BASE_URL"] = "https://example.invalid";
            process.Environment["META_API_KEY"] = "example-ambient-key"; process.Environment["ANTHROPIC_BASE_URL"] = "https://example.invalid";
            NativeConnections.Isolate(process, vendor, account.Profile);
            process.Environment["OpenAI_Api_Key"] = "example-mixed-key"; process.Environment["Anthropic_Base_Url"] = "https://example.invalid";
            NativeConnections.Isolate(process, vendor, account.Profile);
            check(!process.Environment.Keys.Any(key => key.Equals("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) || key.Equals("ANTHROPIC_BASE_URL", StringComparison.OrdinalIgnoreCase)),
                vendor + ": mixed-case ambient secrets and endpoints are stripped on Windows too");
            check(!process.Environment.ContainsKey("CLOUDSDK_AUTH_ACCESS_TOKEN") && !process.Environment.ContainsKey("DASHSCOPE_BASE_URL") && !process.Environment.ContainsKey("META_API_KEY") && !process.Environment.ContainsKey("ANTHROPIC_BASE_URL"), vendor + ": ambient credentials and endpoint overrides stripped");
            check(NativeConnections.Environment(vendor, account.Profile).Values.All(value => value.StartsWith(account.Profile, StringComparison.Ordinal)), vendor + ": profile roots remain scoped to selected account");
        }
        var china = NativeConnections.SiteArguments(account with { Vendor = "alibaba-token", ConnectionOptions = Json("{\"ConsoleSite\":\"china\"}") });
        check(china.Contains("domestic") && china.Contains("cn-beijing"), "Bailian stored China site maps to documented domestic CLI value");
    }
}
