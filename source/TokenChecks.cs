using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class TokenChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var start = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero); var end = start.AddDays(4);
        var account = new Identity("token-example", "openai-api", "Example", Path.GetTempPath(), true, true, 60, false);
        Reading Sample(long count, bool partial = false) => new(account.Key, [], null, "Verbunden", end,
            Tokens: new(count, start, end, "Testmonat", 100, 20, partial));
        foreach (var (count, expected) in new[] { (0L, "0 Token"), (999L, "999 Token"), (1000L, "1k Token"), (12345L, "12,3k Token"), (999999L, "1M Token"), (1234567L, "1,2M Token"), (2000000000L, "2Mrd Token") })
            check(ReadingPresentation.Tokens(Sample(count)) == expected, "Compact token value with unit: " + count);
        check(ReadingPresentation.Tokens(Sample(12345, true)) == "≥ 12,3k Token", "Incomplete count is visibly a lower bound");
        check(ReadingPresentation.Tokens(Sample(999999, true)) == "≥ 999,9k Token", "Compact lower bound never rounds above actually observed token usage");
        check(ReadingPresentation.Tokens(null) == "Token n. v." && ReadingPresentation.Tokens(Sample(-1)) == "Token n. v.", "Missing or invalid token counts never become zero");
        check(ReadingPresentation.TokenDetail(Sample(12345) with { Failed = true }).Contains("12.345 Token")
            && ReadingPresentation.TokenDetail(Sample(12345) with { Failed = true }).Contains("UTC")
            && ReadingPresentation.TokenDetail(Sample(12345) with { Failed = true }).Contains("fehlgeschlagen"), "Token tooltip retains exact count, period and stale state");
        var empty = new MeterSummary(0, 0, null, null, null, 0, null, null, null);
        check(LocalMeterSource.TokenCounter(empty, start, end)?.Total == 0, "Configured local meter with no requests has measured local zero");
        check(LocalMeterSource.TokenCounter(empty with { Requests = 1, UnknownTokenRequests = 1 }, start, end) is null, "Requests without token metadata do not create a zero count");
        check(LocalMeterSource.TokenCounter(empty with { Requests = 2, Total = 20, UnknownTokenRequests = 1 }, start, end) is { Total: 20, Partial: true }, "Local meter exposes partial totals without hiding missing usage");
        check(LocalMeterSource.TokenCounter(empty, start, end, true) is null, "Recording failure cannot report an empty zero month");
        var organization = ApiAccountSource.ParseOpenAi(account, JsonNode.Parse("[{\"results\":[{\"input_tokens\":100,\"output_tokens\":20}]}]")!.AsArray(), [], start, end);
        check(organization.Tokens is { Total: 120, Input: 100, Output: 20, Partial: true } && organization.Tokens.From == start && organization.Tokens.To == end,
            "Organization text counters keep exact requested period and mark account coverage as partial");
        var usage = JsonNode.Parse("[{\"usage\":{\"uncached_input_tokens\":20,\"cache_read_input_tokens\":30,\"cache_creation\":{\"ephemeral_1h_input_tokens\":5,\"ephemeral_5m_input_tokens\":5},\"output_tokens\":40,\"total_tokens\":100}}]")!.AsArray();
        check(AnthropicAnalyticsSource.Parse(account, usage, [], start, end, end, null).Tokens is { Total: 100, Input: 60, Output: 40 }, "Anthropic counter includes cache exactly once");
        check(AnthropicAnalyticsSource.Parse(account, [], [], start, end, null, null).Tokens is null, "Anthropic without a usage snapshot remains unknown");
        check(AnthropicAnalyticsSource.Parse(account, [], [], start, end, start.AddHours(-1), null).Tokens is null, "Previous-month Anthropic snapshot cannot fabricate zero tokens in a new month");
        check(AnthropicAnalyticsSource.Parse(account, [], [], start, end, end.AddHours(-1), null).Tokens is { Total: 0, Partial: true }, "Delayed report snapshot remains visibly incomplete");
        var source = new FlakySource(Sample(12345));
        var clock = new CheckClock();
        using var monitor = new AccountMonitor(_ => source, clock, clock.Delay); var results = new List<Reading>(); monitor.Changed += results.Add;
        await monitor.Refresh([account], true); clock.Advance(TimeSpan.FromSeconds(60)); await monitor.Refresh([account], true);
        check(results.Count == 2 && results[1].Failed && results[1].Tokens?.Total == 12345, "Polling failure preserves token data for the same account as stale");
        await monitor.Authenticate(account, _ => Task.CompletedTask); await monitor.Refresh([account], true);
        check(results[^1].Tokens is null, "Reauthentication cannot leak a previous account token count");
    }
    private sealed class FlakySource(Reading first) : IAccountSource
    {
        private int _calls;
        public Task<Reading> Query(Identity identity, CancellationToken cancellation)
            => ++_calls == 1 ? Task.FromResult(first) : throw new IOException("Fixture failure");
        public void Dispose() { }
    }
}
