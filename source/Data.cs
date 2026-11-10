using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed record Period(string Caption, double Percent, DateTimeOffset? Reset);
internal sealed record TokenUsage(long Total, DateTimeOffset From, DateTimeOffset To, string Scope,
    long? Input = null, long? Output = null, bool Partial = false, bool Lifetime = false);
internal sealed record Reading(string Identity, IReadOnlyList<Period> Periods, string? Balance,
    string Status, DateTimeOffset? Observed, bool Failed = false, long Epoch = 0, string? Detail = null, bool DetailContainsBalance = false, TokenUsage? Tokens = null,
    string? TokenNote = null, DateTimeOffset? NextAttempt = null, int? HttpStatus = null, string? AccountBinding = null, TokenReport? TokenReport = null);
internal sealed record Identity(string Key, string Vendor, string Title, string Profile,
    bool Enabled, bool ShowBalance, int Interval, bool Secondary, bool AutoRenew = true, JsonObject? ConnectionOptions = null);

internal static class ReadingPresentation
{
    internal static double QuotaPercent(Period period, bool remaining)
    {
        var used = Math.Clamp(period.Percent, 0, 100);
        return remaining ? 100 - used : used;
    }
    internal static string QuotaMode(bool remaining) => remaining ? "Verfügbares Kontingent" : "Verbrauchtes Kontingent";
    internal static string Balance(Identity account, Reading? reading)
    {
        var value = reading?.Balance?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value is "-" or "—" or "–") return reading is null ? "Wird abgefragt …" : "Nicht gemeldet";
        if (value.StartsWith("Credits ", StringComparison.Ordinal)) return value[8..] + " Credits";
        return account.Vendor == "claude" ? "Guthaben " + value : value;
    }
    internal static string Countdown(DateTimeOffset? reset, DateTimeOffset? now = null)
    {
        if (reset is null) return "Unbekannt";
        var remaining = reset.Value - (now ?? DateTimeOffset.Now);
        if (remaining <= TimeSpan.Zero) return "0h 0min";
        return (remaining.Days > 0 ? $"{remaining.Days}d " : "") + $"{remaining.Hours}h {remaining.Minutes}min";
    }
    internal static string Detail(Identity account, Reading? reading) => (!account.ShowBalance && reading?.DetailContainsBalance == true
        ? "Beträge ausgeblendet. " + Providers.Get(account.Vendor).Help : reading?.Detail ?? reading?.Status ?? "Wird abgefragt …")
        + (reading?.Failed == true && reading.NextAttempt is DateTimeOffset next ? "\nNächste automatische Abfrage: " + next.ToLocalTime().ToString("dd.MM. HH:mm:ss") : "");
    internal static string Summary(Identity account, Reading? reading) => reading?.Failed == true ? reading.Status
        : !MeterStorage.Enabled(account) && Providers.Get(account.Vendor).Connection == ConnectionKind.Portal ? "Portal · keine automatische Messung"
        : Detail(account, reading).Split('\n')[0];
}

internal static class JsonData
{
    internal static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    internal static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
        if (value.TryGetValue<long>(out var integer)) return integer;
        if (value.TryGetValue<int>(out var small)) return small;
        if (value.TryGetValue<decimal>(out var exact)) return (double)exact;
        return double.TryParse(Text(node), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number) ? number : null;
    }
    internal static bool Flag(JsonNode? node, bool fallback = false) => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;
    internal static DateTimeOffset? Time(JsonNode? node)
    {
        if (Number(node) is double seconds)
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(checked((long)seconds)); }
            catch (ArgumentOutOfRangeException) { return null; }
            catch (OverflowException) { return null; }
        }
        return DateTimeOffset.TryParse(Text(node), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant) ? instant : null;
    }
    internal static Period? Limit(string caption, JsonNode? value, string percent, string reset)
    {
        if (value is not JsonObject obj || Number(obj[percent]) is not double used) return null;
        return new Period(caption, Math.Clamp(used, 0, 100), Time(obj[reset]));
    }
}

internal sealed class Preferences
{
    internal static string DataDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "UseScope")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UseScope");
    private readonly string _path;
    private readonly JsonObject _document;
    internal Preferences(string path)
    {
        _path = path;
        // Invalid settings are surfaced to the user; never replace them with defaults.
        _document = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("Einstellungen haben ein ungültiges Format.") : new JsonObject();
    }
    internal string Theme { get => JsonData.Text(_document["ColorTheme"]) == "Dark" ? "Dark" : "Hell"; set => _document["ColorTheme"] = value == "Dark" ? "Dark" : "Hell"; }
    internal bool ShowRemainingQuota { get => JsonData.Flag(_document["ShowRemainingQuota"]); set => _document["ShowRemainingQuota"] = value; }
    internal bool Pinned { get => JsonData.Flag(_document["AlwaysOnTop"], true); set { _document["AlwaysOnTop"] = value; _document["IsPinned"] = value; } }
    internal string Position { get => JsonData.Text(_document["PopupPosition"]) ?? "BottomRight"; set => _document["PopupPosition"] = value; }
    internal string Shortcut => JsonData.Text(_document["Hotkey"]) ?? "Ctrl+Alt+U";
    internal Point? LastLocation => JsonData.Number(_document["LastPopupX"]) is double x
        && JsonData.Number(_document["LastPopupY"]) is double y && x > int.MinValue && y > int.MinValue
        ? new Point((int)x, (int)y) : null;
    internal void Remember(Point location) { _document["LastPopupX"] = location.X; _document["LastPopupY"] = location.Y; }
    internal IReadOnlyList<Identity> Identities
    {
        get
        {
            var order = (_document["AccountOrder"] as JsonArray ?? new JsonArray()).Select(JsonData.Text).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (_document["Accounts"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(account =>
            {
                var key = JsonData.Text(account["Id"]) ?? "";
                var vendor = JsonData.Text(account["Provider"])?.ToLowerInvariant() ?? "";
                var options = (_document["Providers"] as JsonObject)?[key] as JsonObject;
                var profile = JsonData.Text(options?["ProfileDirectory"]) ?? Path.Combine(DataDirectory, "profiles", key);
                return new Identity(key, vendor, JsonData.Text(account["DisplayName"]) ?? key, profile,
                    JsonData.Flag(options?["Enabled"], true), JsonData.Flag(options?["ShowCredits"], true),
                    Math.Clamp((int)(JsonData.Number(options?["RefreshSeconds"]) ?? 60), 60, 3600),
                    JsonData.Flag(options?["ShowSecondaryRows"]), JsonData.Flag(options?["AutoRenewSession"], true), options?["ConnectionOptions"]?.DeepClone() as JsonObject);
            }).Where(item => item.Key.Length > 0 && item.Key.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && item.Key is not "." and not ".." && Providers.Known(item.Vendor) && seen.Add(item.Key))
              .OrderBy(item => order.IndexOf(item.Key) is var rank && rank >= 0 ? rank : int.MaxValue).ToList();
        }
    }
    internal void SetBalance(Identity identity, bool show)
    {
        var providers = _document["Providers"] as JsonObject ?? new JsonObject();
        if (_document["Providers"] is null) _document["Providers"] = providers;
        var options = providers[identity.Key] as JsonObject ?? new JsonObject();
        if (providers[identity.Key] is null) providers[identity.Key] = options;
        options["ShowCredits"] = show;
        Save();
    }
    internal void SetConnection(Identity identity, JsonObject values)
    {
        var providers = _document["Providers"] as JsonObject ?? throw new InvalidDataException();
        var options = providers[identity.Key] as JsonObject ?? throw new InvalidDataException();
        var old = options["ConnectionOptions"]?.DeepClone(); options["ConnectionOptions"] = values.DeepClone();
        try { Save(); } catch { options["ConnectionOptions"] = old; throw; }
    }
    internal Identity Add(string vendor, string title)
    {
        if (!Providers.Known(vendor)) throw new ArgumentException("Unbekannter Anbieter.");
        var key = "account-" + Guid.NewGuid().ToString("N");
        var list = _document["Accounts"] as JsonArray;
        if (list is null) { list = new JsonArray(); _document["Accounts"] = list; }
        var providers = _document["Providers"] as JsonObject;
        if (providers is null) { providers = new JsonObject(); _document["Providers"] = providers; }
        list.Add(new JsonObject { ["Id"] = key, ["Provider"] = vendor, ["DisplayName"] = title });
        var directory = Path.Combine(DataDirectory, "profiles", key);
        providers[key] = new JsonObject { ["ProfileDirectory"] = directory, ["Enabled"] = true, ["ShowCredits"] = true, ["RefreshSeconds"] = 60 };
        if (Providers.Get(vendor).Connection == ConnectionKind.LocalMeter) providers[key]!["ConnectionOptions"] = MeterSetup.Options();
        Save();
        return Identities.Single(item => item.Key == key);
    }
    internal void Remove(Identity identity)
    {
        if (_document["Accounts"] is JsonArray list)
            for (var index = list.Count - 1; index >= 0; index--)
                if (JsonData.Text(list[index]?["Id"]) == identity.Key) list.RemoveAt(index);
        if (_document["AccountOrder"] is JsonArray order)
            for (var index = order.Count - 1; index >= 0; index--)
                if (JsonData.Text(order[index]) == identity.Key) order.RemoveAt(index);
        (_document["Providers"] as JsonObject)?.Remove(identity.Key);
        Save();
    }
    internal void Move(Identity identity, int direction)
    {
        var keys = Identities.Select(item => item.Key).ToList();
        var from = keys.IndexOf(identity.Key);
        var to = from + direction;
        if (from < 0 || to < 0 || to >= keys.Count) return;
        (keys[from], keys[to]) = (keys[to], keys[from]);
        _document["AccountOrder"] = new JsonArray(keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray());
        Save();
    }
    internal void Reorder(string moved, string target)
    {
        var keys = Identities.Select(identity => identity.Key).ToList();
        var from = keys.IndexOf(moved); var to = keys.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        keys.RemoveAt(from); keys.Insert(Math.Min(to, keys.Count), moved);
        var previous = _document["AccountOrder"]?.DeepClone();
        _document["AccountOrder"] = new JsonArray(keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray());
        try { Save(); } catch { _document["AccountOrder"] = previous; throw; }
    }
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".writing-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, _document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
