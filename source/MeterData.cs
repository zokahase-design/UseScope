using System.Text.Json;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed record MeterCredential(string Key, string LocalToken, string Upstream, string Auth);
internal sealed record MeterQuota(long Limit, long Remaining, DateTimeOffset Reset);
internal sealed record MeterEntry(string Id, DateTimeOffset At, int HttpStatus, long? Input, long? Output, long? Total, string? Model, decimal? Cost, string? Currency, bool Complete, string? ResponseFingerprint = null, MeterQuota? Quota = null);
internal sealed record MeterSummary(long Requests, long Successful, long? Input, long? Output, long? Total, long UnknownTokenRequests, decimal? Cost, string? Currency, DateTimeOffset? Last, MeterQuota? Quota = null);
internal static class MeterStorage
{
    internal static bool Enabled(Identity identity) => JsonData.Text(identity.ConnectionOptions?["Mode"]) == "meter";
    internal static Identity SecretIdentity(Identity identity) => identity with { Key = identity.Key + "-meter", Profile = Path.Combine(identity.Profile, "meter") };
    internal static async Task<MeterCredential?> ReadCredential(Identity identity, CancellationToken cancellation)
    {
        var json = await SecretVault.Read(SecretIdentity(identity), cancellation);
        return json is null ? null : JsonSerializer.Deserialize<MeterCredential>(json) ?? throw new InvalidDataException("Zähler-Zugang unvollständig.");
    }
    internal static Task WriteCredential(Identity identity, MeterCredential value, CancellationToken cancellation) => SecretVault.Write(SecretIdentity(identity), JsonSerializer.Serialize(value), cancellation);
    internal static Task DeleteCredential(Identity identity, CancellationToken cancellation) => SecretVault.Delete(SecretIdentity(identity), cancellation);
}

internal sealed class MeterLedger
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _recordingFailures = new();
    internal void RecordingFailed(Identity identity) => _recordingFailures[identity.Key] = true;
    internal bool HasRecordingFailure(Identity identity) => _recordingFailures.ContainsKey(identity.Key);
    private static string FileName(Identity identity) => Path.Combine(identity.Profile, "meter", "usage.jsonl");
    internal async Task Append(Identity identity, MeterEntry value, CancellationToken cancellation)
    {
        var gate = _gates.GetOrAdd(identity.Key, _ => new(1, 1)); await gate.WaitAsync(cancellation);
        try
        {
            var file = FileName(identity); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await using var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, true);
            if (stream.Length > 0) { stream.Seek(-1, SeekOrigin.End); var last = stream.ReadByte(); stream.Seek(0, SeekOrigin.End); if (last != '\n') await stream.WriteAsync(new byte[] { (byte)'\n' }, cancellation); }
            await using var writer = new StreamWriter(stream); await writer.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), cancellation);
        }
        finally { gate.Release(); }
    }
    internal async Task<MeterSummary> Read(Identity identity, DateTimeOffset start, CancellationToken cancellation, DateTimeOffset? endExclusive = null)
    {
        var gate = _gates.GetOrAdd(identity.Key, _ => new(1, 1)); await gate.WaitAsync(cancellation);
        try
        {
            var file = FileName(identity); if (!File.Exists(file)) return new(0, 0, null, null, null, 0, null, null, null);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true); using var reader = new StreamReader(stream);
            long requests = 0, successful = 0, unknown = 0; long? input = null, output = null, total = null; decimal? cost = null; string? currency = null; DateTimeOffset? last = null; MeterQuota? quota = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var responses = new Dictionary<string, MeterEntry>(StringComparer.Ordinal);
            static long? Higher(long? a, long? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
            static long? PlusDelta(long? sum, long? current, long? previous) => current is null ? sum : checked((sum ?? 0) + Math.Max(0, current.Value - (previous ?? 0)));
            while (await reader.ReadLineAsync(cancellation) is string line)
            {
                if (line.Length > 128_000) throw new InvalidDataException("Zählerdatei ungültig.");
                MeterEntry? entry; try { entry = JsonSerializer.Deserialize<MeterEntry>(line); } catch (JsonException) { throw new InvalidDataException("Zählerdatei enthält einen unvollständigen Eintrag."); }
                if (entry is null || (endExclusive is DateTimeOffset end && entry.At >= end) || !seen.Add(entry.Id)) continue;
                MeterEntry? previous = null;
                if (entry.ResponseFingerprint is string fingerprint) responses.TryGetValue(fingerprint, out previous);
                if (entry.ResponseFingerprint is string responseKey) responses[responseKey] = entry with { Input = Higher(previous?.Input, entry.Input), Output = Higher(previous?.Output, entry.Output), Total = Higher(previous?.Total, entry.Total), Cost = entry.Cost is null ? previous?.Cost : Math.Max(entry.Cost.Value, previous?.Cost ?? 0) };
                if (entry.At < start) continue;
                requests++; if (entry.HttpStatus is >= 200 and < 300 && entry.Complete) successful++;
                input = PlusDelta(input, entry.Input, previous?.Input);
                output = PlusDelta(output, entry.Output, previous?.Output);
                total = PlusDelta(total, entry.Total, previous?.Total);
                if (entry.Total is null) unknown++;
                if (entry.Cost is decimal amount && entry.Currency is string unit)
                { if (currency is not null && currency != unit) throw new InvalidDataException("Zählerwährungen dürfen nicht vermischt werden."); currency = unit; cost = checked((cost ?? 0) + Math.Max(0, amount - (previous?.Cost ?? 0))); }
                if (last is null || entry.At >= last) { last = entry.At; if (entry.Quota is not null) quota = entry.Quota; }
            }
            return new(requests, successful, input, output, total, unknown, cost, currency, last, quota);
        }
        finally { gate.Release(); }
    }
}

internal sealed class LocalMeterSource(MeterLedger ledger) : IAccountSource, ILocalTokenProjection
{
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        if (MeterRuntime.Shared.Failure is string error) return new(identity.Key, [], null, error, null, true);
        if (await MeterStorage.ReadCredential(identity, cancellation) is null) return new(identity.Key, [], null, "Lokalen API-Zähler einrichten", null, true);
        var now = DateTimeOffset.UtcNow; var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero); var value = await ledger.Read(identity, start, cancellation);
        var tokens = value.Total is long count ? count.ToString("N0", ApiAccountSource.German) + " gemeldete Token" : "Token nicht gemeldet";
        var failed = ledger.HasRecordingFailure(identity);
        IReadOnlyList<Period> periods = value.Quota is { Limit: > 0, Remaining: >= 0 } quota && quota.Remaining <= quota.Limit && quota.Reset > now
            ? [new("IFM-Tag", 100.0 * (quota.Limit - quota.Remaining) / quota.Limit, quota.Reset)] : [];
        var reading = new Reading(identity.Key, periods, null, failed ? "Zähler unvollständig · Schreibfehler seit App-Start" : "Lokaler API-Zähler · nur durchgeleitete Anfragen", value.Last, failed,
            Detail: "UTC-Monat · " + value.Requests.ToString("N0", ApiAccountSource.German) + " Anfragen · " + tokens
                + "\nErfolgreich abgeschlossen: " + value.Successful + " · Ohne Tokenmeldung: " + value.UnknownTokenRequests
                + "\n" + (value.Cost is decimal amount && value.Currency is string currency ? "Gemeldete Kosten: " + MeterData.Money(amount, currency) : "Kosten nicht vom Anbieter gemeldet; keine Schätzung.")
                + (periods.Count > 0 ? "\nIFM-Tagesfenster aus zuletzt gemeldeten Rate-Limit-Headern, Reset 00:00 UTC. Keine regelmäßige Gesamtkontoabfrage." : "")
                + "\nKeine Kontogesamtsumme, kein Abo-Restlimit und kein Guthaben. Fremde Apps, alte Verbräuche und abgebrochene Streams können außerhalb der Tokenmessung liegen.", DetailContainsBalance: value.Cost is not null,
            Tokens: TokenCounter(value, start, now, failed));
        return await ProjectTokens(identity, reading, cancellation);
    }
    public async Task<Reading> ProjectTokens(Identity identity, Reading reading, CancellationToken cancellation)
    {
        // Reprojection never opens a network connection or changes the monthly money report.
        if (reading.HttpStatus is 401 or 403 || reading.Observed is null && reading.Failed) return reading;
        var key = TokenPeriods.Selected(identity); var (start, end) = TokenPeriods.Range(key, DateTimeOffset.UtcNow);
        var value = await ledger.Read(identity, start, cancellation, key == TokenPeriods.Previous ? end : end.AddTicks(1));
        var tokens = TokenCounter(value, start, end, ledger.HasRecordingFailure(identity));
        if (tokens is not null) tokens = tokens with { Lifetime = key == TokenPeriods.Total,
            Scope = TokenPeriods.Label(key == TokenPeriods.Default ? TokenPeriods.Month : key) + " · " + tokens.Scope };
        return reading with { Tokens = tokens, TokenNote = tokens is null ? "Keine verwertbaren Tokenmeldungen im gewählten Zeitraum; fehlende Messdaten werden nicht geschätzt." : null };
    }
    internal static TokenUsage? TokenCounter(MeterSummary value, DateTimeOffset start, DateTimeOffset now, bool failed = false)
        => value.Total is long total ? new(total, start, now, "Lokaler API-Zähler · nur durch UseScope geleitete Anfragen im ausgewählten Zeitraum. Ohne Tokenmeldung: " + value.UnknownTokenRequests
                + (failed ? "; Schreibfehler seit App-Start." : "."), value.Input, value.Output, Partial: true)
            : value.Requests == 0 && !failed ? new(0, start, now, "Lokaler API-Zähler · keine aufgezeichneten Anfragen im ausgewählten Zeitraum; Nutzung außerhalb des Zählers ist nicht enthalten.", 0, 0, Partial: true) : null;
    public void Dispose() { }
}
