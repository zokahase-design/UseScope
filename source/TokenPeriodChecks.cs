using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class TokenPeriodChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2032, 3, 1, 0, 30, 0, TimeSpan.FromHours(1)); // Still February in UTC.
        var range = TokenPeriods.Range(TokenPeriods.Previous, now);
        check(range.Start == new DateTimeOffset(2032, 1, 1, 0, 0, 0, TimeSpan.Zero) && range.End == range.Start.AddMonths(1), "Calendar selection normalizes timezone before month boundaries");
        now = new DateTimeOffset(2032, 3, 4, 12, 0, 0, TimeSpan.Zero);
        range = TokenPeriods.Range(TokenPeriods.Previous, now);
        check((range.End - range.Start).TotalDays == 29, "Previous calendar month handles leap years");
        check(TokenPeriods.Range(TokenPeriods.Hours24, now).Start == now.AddHours(-24)
            && TokenPeriods.Range(TokenPeriods.Hours168, now).Start == now.AddHours(-168), "Rolling windows use timestamp precision");
        check(TokenPeriods.Range(TokenPeriods.Calendar7, now).Start == new DateTimeOffset(now.Date.AddDays(-6), TimeSpan.Zero), "Seven calendar days include today and six prior UTC dates");
        var account = new Identity("period-fixture", "codex", "Fixture", "", true, true, 60, false,
            ConnectionOptions: new JsonObject { ["RetainThis"] = "yes" });
        var report = new TokenReport([new(new(2032, 2, 29), 10), new(new(2032, 3, 1), 20), new(new(2032, 3, 4), 30)], 900, now);
        var reading = new Reading(account.Key, [], "9 €", "Fixture", now, TokenReport: report);
        Identity Select(string key) => account with { ConnectionOptions = TokenPeriods.Options(account, key) };
        Reading Project(string key) => TokenPeriods.Project(Select(key), reading, now);
        check(Project(TokenPeriods.Previous).Tokens?.Total == 10, "Codex previous month excludes first day of current month");
        check(Project(TokenPeriods.Month).Tokens?.Total == 50 && Project(TokenPeriods.Today).Tokens?.Total == 30, "Codex month and today project separate daily totals");
        check(Project(TokenPeriods.Calendar7).Tokens is { Total: 60, Partial: true }, "Codex day history remains explicitly partial");
        check(Project(TokenPeriods.Total).Tokens is { Total: 900, Lifetime: true }, "Total preserves provider lifetime rather than sum of partial day history");
        check(Project(TokenPeriods.Hours24).Tokens is null, "Daily buckets cannot fabricate an exact rolling 24-hour count");
        var lifetime = reading with { TokenReport = report with { Days = [] } };
        check(TokenPeriods.Project(Select(TokenPeriods.Month), lifetime, now).Tokens is null
            && TokenPeriods.Available(account, lifetime).Single(option => option.Key == TokenPeriods.Month).Enabled == false,
            "Lifetime-only accounts do not expose invented monthly data");
        check(TokenPeriods.Project(account, lifetime, now).Tokens is { Total: 900, Lifetime: true }, "Default retains lifetime fallback for existing settings");
        var nextMonth = now.AddMonths(1);
        check(TokenPeriods.Project(Select(TokenPeriods.Today), reading, nextMonth).Tokens is null
            && TokenPeriods.Project(Select(TokenPeriods.Previous), reading, nextMonth).Tokens?.Total == 50,
            "Cached day and month selection advances at UTC boundary without relabeling old periods");
        var options = TokenPeriods.Options(account, TokenPeriods.Today);
        check(options["RetainThis"]?.GetValue<string>() == "yes" && account.ConnectionOptions?["TokenPeriod"] is null, "Period setting clones and preserves connection options");
        check(!TokenPeriods.Available(account, reading).Any(option => option.Key == TokenPeriods.Hours24)
            && TokenPeriods.Available(account with { Vendor = "claude" }, null).Any(option => option.Key == TokenPeriods.Hours24), "Period choices reflect daily versus timestamped source capabilities");
        var points = new[] { new LocalTokenReader.Point("old", range.Start, 10), new LocalTokenReader.Point("new", range.End, 100) };
        check(LocalTokenReader.Aggregate(points, range.Start, range.End, true)?.Total == 10, "Local previous month is end-exclusive");
        var source = new CachedSource(reading); var clock = new CheckClock(); clock.Advance(now - clock.GetUtcNow());
        using (var monitor = new AccountMonitor(_ => source, clock, clock.Delay))
        {
            var readings = new List<Reading>(); monitor.Changed += readings.Add;
            await monitor.Refresh([account], true); var due = readings[^1].NextAttempt;
            await monitor.Reproject(Select(TokenPeriods.Today)); await monitor.Refresh([Select(TokenPeriods.Today)], true);
            check(source.Calls == 1 && readings[^1].Tokens?.Total == 30 && readings[^1].NextAttempt == due, "Cached period change avoids provider calls and preserves cooldown");
            await monitor.Authenticate(account, _ => Task.CompletedTask); await monitor.Reproject(Select(TokenPeriods.Total));
            check(readings.Count == 2, "Period reprojection cannot revive data cleared by login change");
        }
        var root = Path.Combine(Path.GetTempPath(), "usescope-period-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var local = account with { Profile = root, ConnectionOptions = new JsonObject { ["Mode"] = "meter" } }; var ledger = new MeterLedger();
            await ledger.Append(local, new("one", range.Start.AddSeconds(-1), 200, 80, 0, 80, null, null, null, true, "same"), default);
            await ledger.Append(local, new("two", range.Start, 200, 100, 0, 100, null, null, null, true, "same"), default);
            await ledger.Append(local, new("three", range.End, 200, 200, 0, 200, null, null, null, true), default);
            var summary = await ledger.Read(local, range.Start, default, range.End);
            check(summary.Total == 20 && summary.Requests == 1, "Meter period boundary retains retry baseline and excludes next month");
            var preferences = new Preferences(Path.Combine(root, "prefs.json")); var added = preferences.Add("codex", "Fixture");
            preferences.SetConnection(added, TokenPeriods.Options(added, TokenPeriods.Previous));
            check(TokenPeriods.Selected(new Preferences(Path.Combine(root, "prefs.json")).Identities.Single()) == TokenPeriods.Previous, "Period preference survives settings reload");
        }
        finally
        {
            var absolute = Path.GetFullPath(root);
            if (Path.GetDirectoryName(absolute) != Path.TrimEndingDirectorySeparator(Path.GetTempPath()) || !Path.GetFileName(absolute).StartsWith("usescope-period-", StringComparison.Ordinal)) throw new InvalidOperationException();
            Directory.Delete(absolute, true);
        }
    }
    private sealed class CachedSource(Reading reading) : IAccountSource
    {
        internal int Calls;
        public Task<Reading> Query(Identity identity, CancellationToken cancellation) { Calls++; return Task.FromResult(reading); }
        public void Dispose() { }
    }
}
