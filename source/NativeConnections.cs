using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class NativeConnections
{
    internal static string Executable(string vendor) => vendor switch {
        "codex" => "codex", "claude" => "claude", "nous" => "hermes", "meta" => "muse",
        "minimax" => "mmx", "alibaba-coding" or "alibaba-token" => "bl", "gemini" => "gcloud",
        _ => throw new ArgumentException("Keine native Verbindung.") };
    internal static string[] Login(string vendor) => vendor switch {
        "codex" => ["-c", "cli_auth_credentials_store='file'", "login"], "claude" => ["auth", "login"],
        "nous" => ["auth", "add", "nous", "--type", "oauth"], "meta" => [],
        "minimax" => ["auth", "login", "--recommend", "--region=global"],
        "alibaba-coding" or "alibaba-token" => ["auth", "login", "--console"],
        "gemini" => ["auth", "login", "--force", "--brief"], _ => throw new ArgumentException() };
    internal static string[] Logout(string vendor) => vendor switch {
        "codex" => ["-c", "cli_auth_credentials_store='file'", "logout"], "claude" => ["auth", "logout"],
        "nous" => ["auth", "logout", "nous"], "meta" => ["logout"], "minimax" => ["auth", "logout"],
        "alibaba-coding" or "alibaba-token" => ["auth", "logout", "--console"], "gemini" => ["auth", "revoke", "--all", "--quiet"], _ => throw new ArgumentException() };
    internal static string[] Login(Identity identity) => Login(identity.Vendor).Concat(SiteArguments(identity)).ToArray();
    internal static string[] SiteArguments(Identity identity)
    {
        if (identity.Vendor is not ("alibaba-coding" or "alibaba-token")) return [];
        var site = JsonData.Text(identity.ConnectionOptions?["ConsoleSite"]) ?? "international";
        return site switch { "international" => ["--console-site", "international", "--console-region", "ap-southeast-1"],
            "china" => ["--console-site", "domestic", "--console-region", "cn-beijing"], _ => throw new InvalidDataException("Alibaba Console-Site ungültig.") };
    }
    internal static void Isolate(ProcessStartInfo process, string vendor, string profile)
    {
        // Remove credential, endpoint and config overrides inherited from the caller.
        foreach (var key in process.Environment.Keys.ToArray())
            if (key.StartsWith("CLOUDSDK_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("BAILIAN_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("MMX_", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("MUSE_", StringComparison.OrdinalIgnoreCase)
                || key.ToUpperInvariant() is "OPENAI_API_KEY" or "CODEX_API_KEY" or "ANTHROPIC_API_KEY" or "ANTHROPIC_AUTH_TOKEN" or "CLAUDE_CODE_OAUTH_TOKEN"
                    or "META_API_KEY" or "MINIMAX_API_KEY" or "MINIMAX_BASE_URL" or "GOOGLE_API_KEY" or "GEMINI_API_KEY" or "GOOGLE_APPLICATION_CREDENTIALS"
                    or "OPENAI_BASE_URL" or "OPENAI_API_BASE" or "ANTHROPIC_BASE_URL"
                || key.StartsWith("DASHSCOPE_", StringComparison.OrdinalIgnoreCase)) process.Environment.Remove(key);
        foreach (var entry in Environment(vendor, profile)) process.Environment[entry.Key] = entry.Value;
    }
    internal static IReadOnlyDictionary<string, string> Environment(string vendor, string profile) => vendor switch {
        "codex" => new Dictionary<string, string> { ["CODEX_HOME"] = profile },
        "claude" => new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = profile, ["CLAUDE_SECURESTORAGE_CONFIG_DIR"] = profile },
        "nous" => new Dictionary<string, string> { ["HERMES_HOME"] = profile },
        "minimax" => new Dictionary<string, string> { ["MMX_CONFIG_DIR"] = profile },
        "alibaba-coding" or "alibaba-token" => new Dictionary<string, string> { ["BAILIAN_CONFIG_DIR"] = profile },
        "gemini" => new Dictionary<string, string> { ["CLOUDSDK_CONFIG"] = profile },
        "meta" => new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = Path.Combine(profile, "config"), ["XDG_DATA_HOME"] = Path.Combine(profile, "data"), ["XDG_CACHE_HOME"] = Path.Combine(profile, "cache") },
        _ => throw new ArgumentException() };
    internal static async Task<string> Read(Identity account, CancellationToken cancellation, params string[] arguments)
    {
        Directory.CreateDirectory(account.Profile);
        using var process = Process.Start(Platform.Cli(account.Vendor, account.Profile, false, arguments)) ?? throw new IOException();
        var output = Bounded(process.StandardOutput, cancellation); var errors = Bounded(process.StandardError, cancellation);
        try
        {
            await process.WaitForExitAsync(cancellation); await errors;
            var result = await output;
            if (process.ExitCode != 0) throw new RemoteFailure("Anbieter-CLI konnte die Abfrage nicht abschließen; Verbindung und Anmeldung prüfen");
            return result;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    internal static async Task<string> Bounded(StreamReader reader, CancellationToken cancellation)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int count; var oversized = false;
        while ((count = await reader.ReadAsync(buffer, cancellation)) > 0)
        { if (result.Length + count > 2_000_000) oversized = true; if (!oversized) result.Append(buffer, 0, count); }
        if (oversized) throw new InvalidDataException("CLI-Antwort zu groß.");
        return result.ToString();
    }
    internal static JsonObject Object(string text) => JsonNode.Parse(text.Trim()) as JsonObject ?? throw new InvalidDataException("Keine maschinenlesbare CLI-Antwort.");
}

internal sealed class PlanSource : IAccountSource
{
    private readonly Func<Identity, CancellationToken, string[], Task<string>> _read;
    internal PlanSource(Func<Identity, CancellationToken, string[], Task<string>>? read = null) => _read = read ?? ((account, cancellation, arguments) => NativeConnections.Read(account, cancellation, arguments));
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var arguments = identity.Vendor == "minimax" ? new[] { "quota", "show", "--output", "json" }
            : new[] { "usage", identity.Vendor == "alibaba-coding" ? "coding-plan" : "token-plan", "--output", "json" };
        arguments = arguments.Concat(NativeConnections.SiteArguments(identity)).ToArray();
        var value = NativeConnections.Object(await _read(identity, cancellation, arguments));
        return identity.Vendor == "minimax" ? MiniMax(identity, value) : Alibaba(identity, value);
    }
    internal static Reading Alibaba(Identity identity, JsonObject value)
    {
        var periods = new List<Period>();
        if (identity.Vendor == "alibaba-coding")
        {
            foreach (var (key, name) in new[] { ("per5Hour", "5h"), ("perWeek", "Woche"), ("perBillMonth", "Monat") })
            {
                if (value[key] is not JsonObject window) continue;
                var fraction = MeterData.Decimal(window["percentage"]); if (fraction < 0) throw new InvalidDataException();
                periods.Add(new(name, Math.Clamp((double)(fraction * 100), 0, 100), MeterData.Milliseconds(window["resetTime"])));
            }
        }
        else
        {
            if (!JsonData.Flag(value["hasQuotaData"])) return new(identity.Key, [], null, "Kein Tokenplan-Kontingent gemeldet", null, true);
            foreach (var property in value)
            {
                var match = System.Text.RegularExpressions.Regex.Match(property.Key, "^(per([1-9][0-9]{0,2})(Hour|Day|Week|Month))Percentage$");
                if (!match.Success || property.Value is null) continue;
                var fraction = MeterData.Decimal(property.Value); if (fraction < 0) throw new InvalidDataException();
                var unit = match.Groups[3].Value switch { "Hour" => "h", "Day" => "d", "Week" => "w", _ => "mo" };
                periods.Add(new(match.Groups[2].Value + unit, Math.Clamp((double)(fraction * 100), 0, 100), MeterData.Milliseconds(value[match.Groups[1].Value + "ResetTime"])));
            }
        }
        if (periods.Count == 0) return new(identity.Key, [], null, "Kein aktiver Plan gemeldet", null, true);
        return new(identity.Key, periods, null, "Plan verbunden · Alibaba", DateTimeOffset.UtcNow,
            Detail: "Planverbrauch · kein Cloud-Guthaben. Console-Login der jeweiligen Alibaba-Site erforderlich. Internationale Planabdeckung hängt von der Hersteller-CLI ab.");
    }
    internal static Reading MiniMax(Identity identity, JsonObject value)
    {
        if (value["base_resp"] is JsonObject status && MeterData.Decimal(status["status_code"]) != 0) throw new RemoteFailure("MiniMax-Plan-Anmeldung oder Kontingent prüfen");
        if (value["model_remains"] is not JsonArray groups) throw new InvalidDataException("MiniMax meldet keine Plangruppen.");
        var periods = new List<Period>(); var notes = new List<string>();
        foreach (var group in groups)
        {
            var name = JsonData.Text(group?["model_name"]) ?? "Plangruppe";
            foreach (var (prefix, caption, reset) in new[] { ("current_interval", "Intervall", "end_time"), ("current_weekly", "Woche", "weekly_end_time") })
            {
                var remaining = group?[prefix + "_remaining_percent"];
                if (remaining is null) continue; // Historical usage_count has conflicting semantics: never infer it.
                if (JsonData.Number(group?[prefix + "_status"]) == 3 && JsonData.Number(group?[prefix + "_total_count"]) == 0)
                { notes.Add(name + " · Verfügbarkeit nicht bestätigt"); continue; }
                var percent = MeterData.Decimal(remaining); if (percent < 0 || percent > 100) throw new InvalidDataException("MiniMax-Prozent ungültig.");
                periods.Add(new(name + " · " + caption, 100 - (double)percent, MeterData.Milliseconds(group?[reset])));
            }
            notes.Add(name);
        }
        if (periods.Count == 0) return new(identity.Key, [], null, "Plan meldet keine eindeutigen Prozentwerte", null, true,
            Detail: "MiniMax-Zähler haben je API-Version unterschiedliche Bedeutung. Hersteller-CLI aktualisieren; Werte werden nicht geraten.");
        return new(identity.Key, periods, null, "Plan verbunden · MiniMax", DateTimeOffset.UtcNow,
            Detail: "Verbrauch aus expliziten Restprozenten · " + string.Join(", ", notes.Distinct()) + ". Vertrag und tatsächlich gemeldete Gruppen bestimmen enthaltene Modelle; kein separates H3-Guthaben.");
    }
    public void Dispose() { }
}
