using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class CodexRpcFailure(int code, RemoteFailure safe) : Exception(safe.Message)
{
    internal int Code { get; } = code;
    internal RemoteFailure Safe { get; } = safe;
}

internal sealed class CodexSource : IAccountSource
{
    private Process? _process;
    private int _sequence;
    private DateTime _authStamp;
    private DateTimeOffset _nextUsage;
    private TokenReport? _report;
    private string? _tokenNote;
    private bool _usageUnsupported;
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var auth = Path.Combine(identity.Profile, "auth.json");
        if (!File.Exists(auth)) { Stop(); _report = null; return new(identity.Key, [], null, "Anmeldung erforderlich", null, true, HttpStatus: 401); }
        var stamp = File.GetLastWriteTimeUtc(auth);
        if (_authStamp != default && stamp != _authStamp) { Stop(); _report = null; _nextUsage = default; }
        _authStamp = stamp;
        try
        {
            if (_process is null || _process.HasExited)
            {
                Stop(); _process = Process.Start(Platform.Cli("codex", identity.Profile, false, "-c", "cli_auth_credentials_store='file'", "app-server")) ?? throw new IOException();
                _ = Drain(_process.StandardError);
                await Call("initialize", new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "usescope_desktop", ["version"] = AppInfo.Version } }, cancellation);
                await _process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(), cancellation);
                await _process.StandardInput.FlushAsync(cancellation);
            }
            var account = await Call("account/read", new JsonObject { ["refreshToken"] = false }, cancellation);
            var limits = await Call("account/rateLimits/read", null, cancellation);
            var reading = Limits.OpenAI(identity.Key, limits, JsonData.Text(account["account"]?["email"]), identity.Secondary);
            int? status = null; DateTimeOffset? retry = null;
            if (!_usageUnsupported && DateTimeOffset.UtcNow >= _nextUsage)
            {
                _nextUsage = DateTimeOffset.UtcNow.AddMinutes(15);
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation); budget.CancelAfter(TimeSpan.FromSeconds(10));
                try { _report = TokenPeriods.Parse(await Call("account/usage/read", null, budget.Token), DateTimeOffset.UtcNow); _tokenNote = null; }
                catch (CodexRpcFailure failure)
                {
                    _usageUnsupported = failure.Code == -32601;
                    _tokenNote = _usageUnsupported ? "Installierte Codex-CLI unterstützt den Tokenbericht nicht. CLI aktualisieren oder lokale Tokenquelle wählen."
                        : "Codex-Tokenbericht vorübergehend nicht verfügbar; Limits werden getrennt angezeigt.";
                    if (failure.Safe.HttpStatus == 429) { status = 429; retry = failure.Safe.Retry ?? DateTimeOffset.UtcNow.AddMinutes(5); _nextUsage = retry.Value > _nextUsage ? retry.Value : _nextUsage; }
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Stop(); _tokenNote = "Codex-Tokenbericht hat das Zeitlimit erreicht; Limits bleiben erhalten."; }
                catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
                { Stop(); _tokenNote = "Codex-Tokenbericht derzeit nicht auswertbar; keine Tokenzahl geschätzt."; }
            }
            return TokenPeriods.Project(identity, reading with { TokenReport = _report is null ? null : _report with { Note = _tokenNote }, TokenNote = _tokenNote, HttpStatus = status, NextAttempt = retry });
        }
        catch (CodexRpcFailure failure) { Stop(); if (failure.Safe.Authentication) { _report = null; _nextUsage = default; } throw failure.Safe; }
        catch { Stop(); throw; }
    }
    private async Task<JsonObject> Call(string method, JsonNode? parameters, CancellationToken token)
    {
        var process = _process ?? throw new IOException(); var id = ++_sequence;
        var request = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters };
        await process.StandardInput.WriteLineAsync(request.ToJsonString().AsMemory(), token); await process.StandardInput.FlushAsync(token);
        while (await process.StandardOutput.ReadLineAsync(token) is string line)
        {
            if (line.Length > 2_000_000) throw new InvalidDataException("Codex-Antwort zu groß.");
            JsonObject? response; try { response = JsonNode.Parse(line) as JsonObject; } catch (System.Text.Json.JsonException) { continue; }
            if (response is null || JsonData.Number(response["id"]) != id) continue;
            if (response["error"] is JsonObject error) throw RpcError(error);
            return response["result"] as JsonObject ?? throw new InvalidDataException("Codex-Antwort unvollständig.");
        }
        throw new IOException("Codex-Verbindung beendet.");
    }
    internal static CodexRpcFailure RpcError(JsonObject error)
    {
        var code = (int)(JsonData.Number(error["code"]) ?? 0);
        // Inspect only known status indicators; never echo raw provider messages, URLs or tokens.
        var message = JsonData.Text(error["message"]) ?? "";
        var data = error["data"] as JsonObject;
        int? status = JsonData.Number(data?["httpStatusCode"]) is double number && number is >= 400 and <= 599 ? (int)number : null;
        if (status is null) foreach (var candidate in new[] { 429, 401, 403, 500, 502, 503, 504 })
            if (System.Text.RegularExpressions.Regex.IsMatch(message, @"\b" + candidate + @"\b")) { status = candidate; break; }
        var auth = status is 401 or 403 || message.Contains("authentication required", StringComparison.OrdinalIgnoreCase) || message.Contains("not logged in", StringComparison.OrdinalIgnoreCase);
        return new(code, new RemoteFailure(status == 429 ? "Codex pausiert die Abfrage (HTTP 429)" : auth ? "Codex-Anmeldung oder Berechtigung prüfen" : "Codex-Abfrage vorübergehend nicht verfügbar",
            status == 429 ? DateTimeOffset.UtcNow.AddMinutes(5) : null, auth, status));
    }
    internal static TokenUsage? ParseUsage(JsonObject response, DateTimeOffset now)
    {
        var identity = new Identity("parse", "codex", "", "", true, true, 60, false);
        return TokenPeriods.Project(identity, new(identity.Key, [], null, "", now, TokenReport: TokenPeriods.Parse(response, now)), now).Tokens;
    }
    private static async Task Drain(StreamReader stream)
    { try { var buffer = new char[2048]; while (await stream.ReadAsync(buffer) > 0) { } } catch (Exception error) when (error is IOException or ObjectDisposedException or InvalidOperationException) { } }
    private void Stop()
    {
        var process = Interlocked.Exchange(ref _process, null); if (process is null) return;
        try { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(250)) process.Kill(true); } } catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }
    public void Dispose() => Stop();
}
