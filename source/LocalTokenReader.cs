using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

// Opt-in only: the UI assigns one local transcript folder to one account.
// Cache contains counters/message hashes only, never prompts, replies or credentials.
internal sealed class LocalTokenReader
{
    internal sealed record Point(string Id, DateTimeOffset At, long Total);
    private sealed record Cached(long Length, DateTime Stamp, IReadOnlyList<Point> Points);
    private readonly Dictionary<string, Cached> _files = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private string? _root;
    private string? _binding;
    internal void Clear() { _files.Clear(); _root = _binding = null; }
    internal static string? Binding(JsonObject? metadata)
    {
        var subject = JsonData.Text(metadata?["accountUuid"]) ?? JsonData.Text(metadata?["emailAddress"])?.Trim().ToLowerInvariant();
        return subject is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject + "|" + JsonData.Text(metadata?["organizationUuid"]))));
    }
    internal static string? DirectoryFor(Identity account) => account.Vendor == "claude" ? JsonData.Text(account.ConnectionOptions?["TokenLogDirectory"]) : null;
    internal static string ValidateDirectory(string path)
    {
        var root = Path.GetFullPath(path);
        if (new Uri(root).IsUnc || !Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0
            || root.TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Einen vorhandenen lokalen Sitzungsordner wählen; keine Laufwerkswurzel oder Verknüpfung.");
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Keine Verknüpfungen oder umgeleiteten Elternordner für Tokenquellen verwenden.");
        if (new DriveInfo(Path.GetPathRoot(root)!).DriveType == DriveType.Network) throw new ArgumentException("Tokenquelle muss auf einem lokalen Laufwerk liegen.");
        return Path.TrimEndingDirectorySeparator(root);
    }
    internal static void EnsureExclusive(Identity account, IReadOnlyList<Identity> accounts, string selected)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
        foreach (var other in accounts.Where(value => value.Key != account.Key))
            if (DirectoryFor(other) is string existing)
            {
                existing = Path.TrimEndingDirectorySeparator(Path.GetFullPath(existing));
                if (root.Equals(existing, comparison) || root.StartsWith(existing + Path.DirectorySeparatorChar, comparison) || existing.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                    throw new ArgumentException("Dieser Ordner überschneidet sich mit der Tokenquelle eines anderen Kontos. Getrennte Konten benötigen getrennte Sitzungsordner.");
            }
    }
    internal async Task<Reading> Apply(Identity account, Reading reading, CancellationToken cancellation)
    {
        var selected = DirectoryFor(account); if (string.IsNullOrWhiteSpace(selected)) return reading;
        var binding = JsonData.Text(account.ConnectionOptions?["TokenLogBinding"]);
        if (binding is null || reading.AccountBinding != binding)
            return reading with { Tokens = null, TokenNote = "Tokenquelle gehört nicht zur bestätigten Anmeldung oder die Anmeldung ist noch nicht bestätigt. Nach erfolgreicher Verbindung die Quelle diesem Konto erneut zuordnen." };
        try
        {
            var root = ValidateDirectory(selected);
            if (root != _root || binding != _binding) { _files.Clear(); _root = root; _binding = binding; }
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation); budget.CancelAfter(TimeSpan.FromSeconds(5));
            var seen = new HashSet<string>(_files.Comparer); long bytes = 0; var count = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false, MaxRecursionDepth = 16 };
            foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", options))
            {
                budget.Token.ThrowIfCancellationRequested();
                if (++count > 2000) break;
                var info = new FileInfo(path); if (info.Length > 64 * 1024 * 1024) continue;
                bytes += info.Length; if (bytes > 128 * 1024 * 1024) break;
                seen.Add(path);
                if (_files.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Stamp == info.LastWriteTimeUtc) continue;
                var points = new List<Point>();
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
                using var reader = new StreamReader(stream);
                while (await reader.ReadLineAsync(budget.Token) is string line)
                {
                    if (line.Length > 1_000_000 || !line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                    var point = Parse(line); if (point is not null) points.Add(point);
                    if (points.Count > 100_000) throw new InvalidDataException();
                }
                _files[path] = new(info.Length, info.LastWriteTimeUtc, points);
            }
            foreach (var stale in _files.Keys.Where(path => !seen.Contains(path)).ToArray()) _files.Remove(stale);
            var now = DateTimeOffset.UtcNow; var key = TokenPeriods.Selected(account); var (start, end) = TokenPeriods.Range(key, now);
            var allPoints = _files.Values.SelectMany(file => file.Points).ToArray();
            if (key == TokenPeriods.Total && allPoints.Length > 0) start = allPoints.Min(point => point.At);
            var value = Aggregate(allPoints, start, end, key == TokenPeriods.Previous);
            if (value is not null) value = value with { Scope = TokenPeriods.Label(key == TokenPeriods.Default ? TokenPeriods.Month : key) + " · " + value.Scope };
            return reading with { Tokens = value, TokenNote = value is null ? "Im gewählten Ordner wurden keine verwertbaren Claude-Code-Tokenmeldungen gefunden. JSONL-Sitzungsordner und Kontozuordnung prüfen." : null };
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { return reading with { Tokens = null, TokenNote = "Lokale Tokenauswertung überschreitet das Zeitlimit. Einen kleineren Sitzungsordner für dieses Konto wählen." }; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or OverflowException)
        { return reading with { Tokens = null, TokenNote = "Lokale Tokenquelle nicht lesbar oder nicht eindeutig auswertbar. Ordner und Kontozuordnung prüfen." }; }
    }
    internal static Point? Parse(string line)
    {
        try
        {
            var entry = JsonNode.Parse(line);
            if (JsonData.Text(entry?["type"]) != "assistant" || entry?["message"] is not JsonObject message || message["usage"] is not JsonObject usage
                || JsonData.Text(message["id"]) is not string id || id.Length is 0 or > 512 || JsonData.Time(entry?["timestamp"]) is not DateTimeOffset at) return null;
            var input = MeterData.Count(usage["input_tokens"]); var output = MeterData.Count(usage["output_tokens"]);
            var read = usage["cache_read_input_tokens"] is JsonNode cache ? MeterData.Count(cache) : 0;
            var creation = usage["cache_creation_input_tokens"] is JsonNode total ? MeterData.Count(total)
                : checked((usage["cache_creation"]?["ephemeral_5m_input_tokens"] is JsonNode shortCache ? MeterData.Count(shortCache) : 0)
                    + (usage["cache_creation"]?["ephemeral_1h_input_tokens"] is JsonNode longCache ? MeterData.Count(longCache) : 0));
            return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))), at, checked(input + output + read + creation));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or InvalidDataException or OverflowException) { return null; }
    }
    internal static TokenUsage? Aggregate(IEnumerable<Point> points, DateTimeOffset start, DateTimeOffset end, bool endExclusive = false)
    {
        var groups = points.Where(point => endExclusive ? point.At < end : point.At <= end).GroupBy(point => point.Id, StringComparer.Ordinal).ToArray();
        if (groups.Length == 0) return null;
        long total = 0;
        foreach (var group in groups)
        {
            var before = group.Where(point => point.At < start).Select(point => point.Total).DefaultIfEmpty(0).Max();
            var inside = group.Where(point => point.At >= start).Select(point => point.Total).DefaultIfEmpty(0).Max();
            total = checked(total + Math.Max(0, inside - before));
        }
        return new(total, start, end, "Lokale Claude-Code-Dateien · vom Nutzer diesem Konto zugeordnet. Nur im gewählten Ordner beobachtete Meldungen, einschließlich Cache. Keine Webnutzung, anderen Geräte oder garantierte Kontogesamtsumme.", Partial: true);
    }
}
