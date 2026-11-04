using System.Globalization;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed record TokenDay(DateOnly Date, long Total);
internal sealed record TokenReport(IReadOnlyList<TokenDay> Days, long? Lifetime, DateTimeOffset Observed, string? Note = null);
internal sealed record TokenPeriodOption(string Key, string Label, bool Enabled = true);
internal interface ILocalTokenProjection
{
    Task<Reading> ProjectTokens(Identity identity, Reading reading, CancellationToken cancellation);
}

internal static class TokenPeriods
{
    internal const string Default = "default", Today = "today", Calendar7 = "calendar7", Month = "month", Previous = "previous", Total = "total", Hours24 = "24h", Hours168 = "168h";
    internal static string Selected(Identity account) => JsonData.Text(account.ConnectionOptions?["TokenPeriod"]) is string key
        && new[] { Today, Calendar7, Month, Previous, Total, Hours24, Hours168 }.Contains(key) ? key : Default;
    internal static JsonObject Options(Identity account, string key)
    {
        var options = account.ConnectionOptions?.DeepClone().AsObject() ?? new JsonObject();
        if (key == Default) options.Remove("TokenPeriod"); else options["TokenPeriod"] = key;
        return options;
    }
    internal static string Label(string key) => key switch {
        Today => "Heute (UTC)", Calendar7 => "Letzte 7 Kalendertage (UTC)", Month => "Laufender Monat (UTC)", Previous => "Vormonat (UTC)",
        Total => "Gesamt · gemeldete Daten", Hours24 => "Letzte 24 Stunden", Hours168 => "Letzte 7 × 24 Stunden", _ => "Anbieterstandard" };
    internal static (DateTimeOffset Start, DateTimeOffset End) Range(string key, DateTimeOffset now)
    {
        now = now.ToUniversalTime();
        var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return key switch {
            Today => (today, now), Calendar7 => (today.AddDays(-6), now), Previous => (month.AddMonths(-1), month),
            Total => (DateTimeOffset.MinValue, now), Hours24 => (now.AddHours(-24), now), Hours168 => (now.AddHours(-168), now), _ => (month, now) };
    }
    internal static IReadOnlyList<TokenPeriodOption> Available(Identity account, Reading? reading)
    {
        var local = account.Vendor == "claude" || AccountMonitor.Local(account);
        if (!local && account.Vendor != "codex") return [new(Default, "Anbieterbericht · fester Zeitraum")];
        var days = reading?.TokenReport?.Days.Count > 0;
        var list = new List<TokenPeriodOption> { new(Default, Label(Default)) };
        foreach (var key in new[] { Today, Calendar7, Month, Previous, Total })
            list.Add(new(key, Label(key), local || (key == Total ? reading?.TokenReport?.Lifetime is not null || days : days)));
        if (local) { list.Add(new(Hours24, Label(Hours24))); list.Add(new(Hours168, Label(Hours168))); }
        return list;
    }
    internal static TokenReport Parse(JsonObject response, DateTimeOffset now)
    {
        var days = new Dictionary<DateOnly, long>();
        if (response["dailyUsageBuckets"] is JsonArray buckets)
            foreach (var bucket in buckets)
            {
                if (!DateOnly.TryParseExact(JsonData.Text(bucket?["startDate"]), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    || !days.TryAdd(date, MeterData.Count(bucket?["tokens"]))) throw new InvalidDataException("Ungültige oder doppelte Codex-Tageswerte.");
            }
        return new(days.Select(day => new TokenDay(day.Key, day.Value)).ToArray(),
            response["summary"]?["lifetimeTokens"] is JsonNode lifetime ? MeterData.Count(lifetime) : null, now);
    }
    internal static Reading Project(Identity account, Reading reading, DateTimeOffset? now = null)
    {
        if (account.Vendor != "codex" || AccountMonitor.Local(account)) return reading;
        if (reading.TokenReport is not TokenReport report) return reading;
        var instant = now ?? DateTimeOffset.UtcNow;
        var key = Selected(account);
        if (key == Default)
        {
            var range = Range(Month, instant);
            key = report.Days.Any(day => At(day) >= range.Start && At(day) <= range.End) ? Month : Total;
        }
        TokenUsage? tokens = null;
        if (key == Total && report.Lifetime is long lifetime)
            tokens = new(lifetime, DateTimeOffset.MinValue, report.Observed, "Codex-Konto · gemeldete lifetimeTokens.", Lifetime: true);
        else if (key is not (Hours24 or Hours168))
        {
            var (start, end) = Range(key, instant);
            var observedEnd = report.Observed < end ? report.Observed : end;
            var days = report.Days.Where(day => At(day) >= start && At(day) <= observedEnd && (key != Previous || At(day) < end)).ToArray();
            if (days.Length > 0 && observedEnd >= start)
                tokens = new(days.Aggregate(0L, (sum, day) => checked(sum + day.Total)), key == Total ? days.Min(At) : start, observedEnd,
                    "Codex-Konto · " + Label(key) + ". Summe gelieferter Tageswerte; nicht gemeldete Tage und Anbieter-Verzögerungen können fehlen.", Partial: true);
        }
        return reading with { Tokens = tokens, TokenNote = tokens is null
            ? Label(key) + ": Codex meldet keine passenden Tageswerte. Eine Gesamtsumme erlaubt keine Aufteilung auf diesen Zeitraum."
            : report.Note };
    }
    private static DateTimeOffset At(TokenDay day) => new(day.Date.Year, day.Date.Month, day.Date.Day, 0, 0, 0, TimeSpan.Zero);
}
