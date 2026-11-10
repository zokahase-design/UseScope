using System.Net;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class NativeUsageChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow; var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        string Message(string id, DateTimeOffset at, long output) => new JsonObject { ["type"] = "assistant", ["timestamp"] = at.ToString("O"), ["message"] = new JsonObject {
            ["id"] = id, ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = output, ["cache_read_input_tokens"] = 20,
                ["cache_creation_input_tokens"] = 4, ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 4 } } } }.ToJsonString();
        var first = LocalTokenReader.Parse(Message("message-a", now, 5));
        check(first?.Total == 39, "Local Claude counters include cache creation and read exactly once");
        check(LocalTokenReader.Parse("{\"type\":\"user\",\"message\":{\"usage\":{\"input_tokens\":999}}}") is null && LocalTokenReader.Parse("not-json") is null,
            "Local parser ignores prompts and malformed records instead of manufacturing counts");
        check(LocalTokenReader.Aggregate([first!, first!], start, now)?.Total == 39, "Copied or streamed duplicate message IDs are not charged twice");
        var before = LocalTokenReader.Parse(Message("boundary", start.AddSeconds(-1), 0))!;
        var after = LocalTokenReader.Parse(Message("boundary", start, 5))!;
        check(LocalTokenReader.Aggregate([before, after, after], start, now)?.Total == 5, "Streaming snapshot across month boundary contributes only newly observed tokens");
        check(LocalTokenReader.Aggregate([], start, now) is null, "No local token records is unknown rather than a false zero");
        check(LocalTokenReader.Aggregate([before], start, now) is { Total: 0, Partial: true }, "Recognized old local records produce a scoped current-month zero");
        var temporaryParent = Path.GetTempPath();
        // macOS /var is an alias of /private/var; fixtures must use the physical path too.
        if (OperatingSystem.IsMacOS() && temporaryParent.StartsWith("/var/", StringComparison.Ordinal)) temporaryParent = "/private" + temporaryParent;
        var root = Path.Combine(temporaryParent, "usescope-native-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var binding = LocalTokenReader.Binding(new JsonObject { ["accountUuid"] = "fixture-a", ["organizationUuid"] = "fixture-org" })!;
            var account = new Identity("local-usage-example", "claude", "Fixture", root, true, true, 60, false,
                ConnectionOptions: new JsonObject { ["TokenLogDirectory"] = root, ["TokenLogBinding"] = binding, ["FixtureOption"] = true });
            var file = Path.Combine(root, "session.jsonl"); await File.WriteAllTextAsync(file, Message("message-a", now, 5) + "\n");
            var reading = new Reading(account.Key, [], null, "Fixture", now, AccountBinding: binding);
            var reader = new LocalTokenReader(); var counted = await reader.Apply(account, reading, default);
            check(counted.Tokens is { Total: 39, Partial: true }, "Explicitly bound local account reads its chosen token folder");
            var disconnected = account with { ConnectionOptions = LocalTokenReader.WithoutDirectory(account) };
            check(LocalTokenReader.DirectoryFor(account) == root && LocalTokenReader.DirectoryFor(disconnected) is null
                && !disconnected.ConnectionOptions!.ContainsKey("TokenLogBinding") && disconnected.ConnectionOptions["FixtureOption"]!.GetValue<bool>()
                && (await reader.Apply(disconnected, reading, default)).Tokens is null,
                "Disconnecting Claude history stops local reads and preserves unrelated account options");
            var again = await reader.Apply(account, reading, default); check(again.Tokens?.Total == 39, "Repeated local reads do not accumulate the same history again");
            await File.AppendAllTextAsync(file, Message("message-a", now, 9) + "\n");
            check((await reader.Apply(account, reading, default)).Tokens?.Total == 43, "Growing streaming messages update one counter instead of duplicating usage");
            check((await reader.Apply(account, reading with { AccountBinding = "different-login" }, default)).Tokens is null, "Changed login cannot reuse the previous account's local token mapping");
            var other = account with { Key = "other-account" }; var rejected = false;
            try { LocalTokenReader.EnsureExclusive(other, [account], Path.Combine(root, "subfolder")); } catch (ArgumentException) { rejected = true; }
            check(rejected, "Nested folders cannot be silently counted for multiple accounts");
            check(Directory.GetFiles(root).Length == 1 && !Directory.GetFiles(root).Any(path => path.EndsWith(".cache")), "Local parser creates no prompt or transcript copies");

            var authChecks = 0; var credential = new JsonObject { ["accessToken"] = "fixture-access-token", ["expiresAt"] = now.AddHours(2).ToUnixTimeMilliseconds() };
            var requests = 0;
            using (var source = new ClaudeSource(new JsonTransport(new Handler(_ => { requests++; return Json("{\"five_hour\":{\"utilization\":12}}"); })),
                (_, _) => Task.FromResult<JsonObject?>(credential.DeepClone().AsObject()), (_, _) => { authChecks++; return Task.CompletedTask; }))
            {
                var result = await source.Query(account, default);
                check(requests == 1 && authChecks == 0 && result.Periods.Count == 1, "Healthy Claude polling performs one usage read and starts no auth or MCP process");
            }
            requests = 0; authChecks = 0;
            using (var source = new ClaudeSource(new JsonTransport(new Handler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.Unauthorized); })),
                (_, _) => Task.FromResult<JsonObject?>(credential.DeepClone().AsObject()), (_, _) => { authChecks++; return Task.CompletedTask; }))
            {
                for (var index = 0; index < 2; index++) try { await source.Query(account, default); } catch (RemoteFailure) { }
                check(requests == 2 && authChecks == 1, "Unchanged invalid Claude token is never replayed within a query and auth checks are throttled");
            }
            credential["expiresAt"] = now.AddHours(-1).ToUnixTimeMilliseconds(); requests = 0;
            check(JsonData.Number(credential["expiresAt"]) == now.AddHours(-1).ToUnixTimeMilliseconds(), "Native integer credential expiration is recognized without JSON roundtrip");
            using (var source = new ClaudeSource(new JsonTransport(new Handler(_ => { requests++; return Json("{}"); })),
                (_, _) => Task.FromResult<JsonObject?>(credential.DeepClone().AsObject()), (_, _) => Task.CompletedTask))
            {
                var expired = false; try { await source.Query(account, default); } catch (RemoteFailure failure) { expired = failure.Authentication; }
                check(expired && requests == 0, "Known expired Claude credential does not generate a rejected upstream request");
            }
            credential["expiresAt"] = now.AddHours(2).ToUnixTimeMilliseconds(); var phase = 0; var wallets = 0;
            var organization = Guid.NewGuid().ToString();
            async Task WriteMetadata() => await File.WriteAllTextAsync(Path.Combine(root, ".claude.json"), new JsonObject { ["oauthAccount"] = new JsonObject {
                ["accountUuid"] = "fixture-" + phase, ["emailAddress"] = "fixture" + phase + "@example.test", ["organizationUuid"] = organization } }.ToJsonString());
            await WriteMetadata();
            using (var source = new ClaudeSource(new JsonTransport(new Handler(request => {
                if (request.RequestUri!.AbsolutePath.EndsWith("/usage")) return Json("{\"five_hour\":{\"utilization\":12}}");
                wallets++; return Json("{\"balance\":{\"money\":{\"amount_minor\":" + (phase == 0 ? 100 : 200) + ",\"currency\":\"EUR\",\"exponent\":2}}}");
            })), (_, _) => Task.FromResult<JsonObject?>(credential.DeepClone().AsObject()), (_, _) => Task.CompletedTask))
            {
                var old = await source.Query(account, default); phase = 1; await WriteMetadata(); var changed = await source.Query(account, default);
                check(wallets == 2 && old.Balance == "1,00 €" && changed.Balance == "2,00 €" && old.AccountBinding != changed.AccountBinding,
                    "External Claude account switch clears old wallet cache immediately");
            }
        }
        finally
        {
            var absolute = Path.GetFullPath(root);
            if (Path.GetDirectoryName(absolute) != Path.TrimEndingDirectorySeparator(temporaryParent) || !Path.GetFileName(absolute).StartsWith("usescope-native-check-", StringComparison.Ordinal)) throw new InvalidOperationException();
            Directory.Delete(absolute, true);
        }
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }
}
