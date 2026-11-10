using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class CheckClock : TimeProvider
{
    private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    internal void Advance(TimeSpan value) => Interlocked.Add(ref _ticks, value.Ticks);
    internal Task Delay(TimeSpan value, CancellationToken token) { token.ThrowIfCancellationRequested(); Advance(value); return Task.CompletedTask; }
}
internal static class PollingChecks
{
    private sealed class Source(Func<Identity, CancellationToken, Task<Reading>> query) : IAccountSource
    {
        internal int Calls; internal bool Disposed;
        public Task<Reading> Query(Identity account, CancellationToken token) { Calls++; return query(account, token); }
        public void Dispose() { Disposed = true; }
    }
    internal static async Task Run(Action<bool, string> check)
    {
        var a = new Identity("poll-a", "claude", "Fixture", Path.GetTempPath(), true, true, 60, false);
        var b = a with { Key = "poll-b" }; var clock = new CheckClock();
        Reading Ok(Identity account) => new(account.Key, [new("5h", 10, clock.GetUtcNow().AddHours(2))], "1 €", "Verbunden", clock.GetUtcNow());
        check(AccountMonitor.Interval(a).TotalSeconds == 300 && AccountMonitor.Interval(a with { Vendor = "deepseek" }).TotalSeconds == 600, "Native and billing polling use conservative minimum intervals");
        check(AccountMonitor.Interval(a with { ConnectionOptions = MeterSetup.Options() }).TotalSeconds == 15, "Local-only meter refreshes without extra provider connections");
        check(AccountMonitor.ProviderKey(a) == AccountMonitor.ProviderKey(a with { Vendor = "anthropic-api" }), "Related provider products share a request gate");
        var healthy = new Source((account, _) => Task.FromResult(Ok(account)));
        using (var monitor = new AccountMonitor(_ => healthy, clock, clock.Delay))
        {
            await monitor.Refresh([a], true); await monitor.Refresh([a], true); await monitor.Refresh([a], false);
            check(healthy.Calls == 1, "Rapid manual refresh cannot bypass sixty-second request floor");
            clock.Advance(TimeSpan.FromSeconds(60)); await monitor.Refresh([a], true);
            clock.Advance(TimeSpan.FromSeconds(299)); await monitor.Refresh([a], false);
            check(healthy.Calls == 2, "Automatic polls wait a full interval after the last completed query");
            clock.Advance(TimeSpan.FromSeconds(1)); await monitor.Refresh([a], false); check(healthy.Calls == 3, "Due account becomes eligible again");
        }
        check(healthy.Disposed, "Monitor disposal synchronously disposes idle helper sources");
        clock = new CheckClock(); var calls = 0; var starts = new List<DateTimeOffset>(); var active = 0; var peak = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var monitor = new AccountMonitor(_ => new Source(async (account, token) => {
            var running = Interlocked.Increment(ref active); peak = Math.Max(peak, running); starts.Add(clock.GetUtcNow());
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await release.Task.WaitAsync(token); }
            Interlocked.Decrement(ref active); return Ok(account);
        }), clock, clock.Delay))
        {
            var refresh = monitor.Refresh([a, b], true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            check(calls == 1, "Second account cannot open a simultaneous connection to the same provider");
            release.SetResult(); await refresh;
            check(peak == 1 && starts.Count == 2 && starts[1] - starts[0] >= TimeSpan.FromSeconds(15), "Same-provider queries are serialized and spaced fifteen seconds apart");
        }
        clock = new CheckClock(); var throttled = new Source((_, _) => throw new RemoteFailure("Fixture throttle", clock.GetUtcNow().AddMinutes(7), httpStatus: 429));
        var untouched = new Source((account, _) => Task.FromResult(Ok(account))); var reports = new List<Reading>();
        using (var monitor = new AccountMonitor(account => account.Key == a.Key ? throttled : untouched, clock, clock.Delay))
        {
            monitor.Changed += reports.Add; await monitor.Refresh([a, b], true);
            check(throttled.Calls == 1 && untouched.Calls == 0 && reports.All(result => result.HttpStatus == 429), "429 pauses all accounts at that provider before more network requests");
            clock.Advance(TimeSpan.FromMinutes(6)); await monitor.Refresh([a, b], true);
            check(throttled.Calls == 1 && untouched.Calls == 0, "Manual refresh respects provider Retry-After");
            clock.Advance(TimeSpan.FromMinutes(1)); await monitor.Refresh([a], true);
            check(throttled.Calls == 2 && reports[^1].NextAttempt >= clock.GetUtcNow().AddMinutes(10), "Repeated failures increase the cooldown");
        }
        clock = new CheckClock(); var failedAuth = new Source((_, _) => throw new RemoteFailure("Login", authentication: true, httpStatus: 401));
        untouched = new Source((account, _) => Task.FromResult(Ok(account)));
        using (var monitor = new AccountMonitor(account => account.Key == a.Key ? failedAuth : untouched, clock, clock.Delay))
        {
            await monitor.Refresh([a, b], true); clock.Advance(TimeSpan.FromMinutes(5)); await monitor.Refresh([a], false);
            check(failedAuth.Calls == 1 && untouched.Calls == 1, "Broken login pauses only its account and does not hammer authentication");
        }
        clock = new CheckClock(); calls = 0; reports.Clear();
        using (var monitor = new AccountMonitor(_ => new Source((account, _) => {
            if (++calls > 1) throw new RemoteFailure("Login changed", authentication: true, httpStatus: 401);
            return Task.FromResult(Ok(account) with { AccountBinding = "old-login", Tokens = new(12, clock.GetUtcNow().AddDays(-1), clock.GetUtcNow(), "Fixture") });
        }), clock, clock.Delay))
        {
            monitor.Changed += reports.Add; await monitor.Refresh([a], true); clock.Advance(TimeSpan.FromSeconds(60)); await monitor.Refresh([a], true);
            check(reports[^1] is { Failed: true, Tokens: null, AccountBinding: null, Balance: null } && reports[^1].Periods.Count == 0,
                "Authentication loss clears old tokens, binding and limits instead of reattaching old account data");
        }
        clock = new CheckClock(); active = 0; peak = 0; calls = 0;
        reports.Clear();
        using (var monitor = new AccountMonitor(_ => new Source((account, _) => Task.FromResult(++calls == 1 ? Ok(account)
            : new Reading(account.Key, [], null, "Kein aktiver Plan gemeldet", null, true))), clock, clock.Delay))
        {
            monitor.Changed += reports.Add; await monitor.Refresh([a], true); clock.Advance(TimeSpan.FromSeconds(60)); await monitor.Refresh([a], true);
            check(reports[^1].Periods.Count == 0 && reports[^1].Balance is null, "Explicit no-plan or missing-configuration source result clears old values instead of reviving stale limits");
        }
        active = 0; peak = 0; calls = 0;
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var monitor = new AccountMonitor(_ => new Source(async (account, token) => {
            var running = Interlocked.Increment(ref active); peak = Math.Max(peak, running);
            if (Interlocked.Increment(ref calls) == 2) entered.TrySetResult(); await release.Task.WaitAsync(token);
            Interlocked.Decrement(ref active); return Ok(account);
        }), clock, clock.Delay))
        {
            var refresh = monitor.Refresh([a with { Vendor = "deepseek" }, b with { Vendor = "moonshot" }, a with { Key = "poll-c", Vendor = "openrouter" }], true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); check(calls == 2, "Global gate admits at most two provider queries"); release.SetResult(); await refresh;
            check(peak == 2, "Global request concurrency remains bounded across providers");
        }
        var now = DateTimeOffset.UtcNow;
        var usage = CodexSource.ParseUsage(JsonNode.Parse("{\"summary\":{\"lifetimeTokens\":1234567},\"dailyUsageBuckets\":null}")!.AsObject(), now);
        check(usage is { Total: 1234567, Lifetime: true }, "Codex lifetime activity retains its lifetime flag");
        var day = now.ToString("yyyy-MM-dd");
        usage = CodexSource.ParseUsage(JsonNode.Parse("{\"dailyUsageBuckets\":[{\"startDate\":\"" + day + "\",\"tokens\":45}]}")!.AsObject(), now);
        check(usage is { Total: 45, Partial: true, Lifetime: false }, "Codex monthly buckets count only observed current-month days");
        check(CodexSource.ParseUsage(JsonNode.Parse("{\"summary\":{\"lifetimeTokens\":null},\"dailyUsageBuckets\":[]}")!.AsObject(), now) is null, "Missing Codex activity remains unknown");
        var rejected = false; try { CodexSource.ParseUsage(JsonNode.Parse("{\"summary\":{\"lifetimeTokens\":-1}}")!.AsObject(), now); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Invalid Codex token counters are rejected");
        var rpc = CodexSource.RpcError(JsonNode.Parse("{\"code\":-32603,\"message\":\"HTTP 429 private-token-value\"}")!.AsObject());
        check(rpc.Safe.HttpStatus == 429 && !rpc.Message.Contains("private-token-value"), "Codex errors retain safe status without leaking raw payloads");
    }
}
