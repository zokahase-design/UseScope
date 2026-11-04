using System.Diagnostics;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class MuseSource : IAccountSource
{
    private static long _nextRefresh;
    private static bool ReserveRefresh()
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        while (true) { var next = Interlocked.Read(ref _nextRefresh); if (now < next) return false; if (Interlocked.CompareExchange(ref _nextRefresh, now + TimeSpan.FromSeconds(65).Ticks, next) == next) return true; }
    }
    internal static Reading Parse(Identity identity, JsonObject result, bool refreshed)
    {
        if (result["usage"] is not JsonObject usage) return new(identity.Key, [], null,
            result["subscriptionActive"] is JsonValue active && active.TryGetValue<bool>(out var present) && !present ? "Muse · kein aktives Abo gemeldet" : "Muse meldet noch kein Kontingent", null, true,
            Detail: "Muse Code anmelden und aktuelle CLI verwenden. usage/read enthält nur bereits beobachtete Werte; ein fehlendes Kontingent ist keine Nullquote.");
        var observed = MeterData.Milliseconds(usage["observedAtMs"]) ?? throw new InvalidDataException("Muse-Datenstand fehlt.");
        var periods = new List<Period>();
        foreach (var (key, title) in new[] { ("window", "Fenster"), ("weekly", "Woche") })
        {
            if (usage[key] is not JsonObject window) throw new InvalidDataException("Muse-Nutzungsfenster fehlt.");
            var used = MeterData.Decimal(window["usedPercent"]); if (used < 0) throw new InvalidDataException();
            var caption = title;
            if (key == "window" && JsonData.Number(window["windowDurationMins"]) is double minutes && minutes > 0) caption = (minutes / 60).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "h";
            periods.Add(new(caption, Math.Clamp((double)used, 0, 100), MeterData.Milliseconds(window["resetsAtMs"])));
        }
        return new(identity.Key, periods, null, refreshed ? "Muse-Abo · Kontingent abgefragt" : "Muse-Abo · letzter gemeldeter Stand", observed,
            Detail: "Muse-Code-Abo · Tarif " + (JsonData.Text(usage["tier"]) ?? "nicht gemeldet") + ". Gemeinsame Kontingente der im Tarif enthaltenen Modelle, keine getrennte Muse-Image-/Wonder-Rechnung."
            + (refreshed ? "\nFrische Abfrage über experimentelles usage/refresh; Herstellerprotokoll-Version beachten." : "\nDie CLI unterstützt keine frische Protokollabfrage; usage/read liefert nur beobachtete Werte.")
            + "\nDatenstand vom Anbieter: " + observed.ToString("u"));
    }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        Directory.CreateDirectory(identity.Profile);
        using var process = Process.Start(Platform.Cli(identity.Vendor, identity.Profile, false, "serve")) ?? throw new IOException();
        var errors = NativeConnections.Bounded(process.StandardError, cancellation); var id = 0;
        async Task<JsonObject> Call(string method, JsonObject parameters)
        {
            var current = ++id; await process.StandardInput.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = current, ["method"] = method, ["params"] = parameters }.ToJsonString().AsMemory(), cancellation);
            await process.StandardInput.FlushAsync(cancellation);
            for (var messages = 0; messages < 512; messages++)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellation) ?? throw new IOException("Muse-Protokoll beendet.");
                if (line.Length > 2_000_000) throw new InvalidDataException();
                var response = JsonNode.Parse(line) as JsonObject ?? throw new InvalidDataException();
                if (JsonData.Number(response["id"]) != current) continue;
                if (response["error"] is JsonObject error) throw new MuseProtocolException(checked((int)MeterData.Decimal(error["code"])));
                return response["result"] as JsonObject ?? throw new InvalidDataException("Muse-Ergebnis fehlt.");
            }
            throw new InvalidDataException("Muse-Protokollantwort fehlt.");
        }
        try
        {
            await Call("initialize", new() { ["clientInfo"] = new JsonObject { ["name"] = "usescope", ["version"] = "2.2.0" }, ["capabilities"] = new JsonObject { ["experimentalApi"] = true } });
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\"}".AsMemory(), cancellation); await process.StandardInput.FlushAsync(cancellation);
            if (!ReserveRefresh()) return Parse(identity, await Call("usage/read", new()), false);
            try { return Parse(identity, await Call("usage/refresh", new()), true); }
            catch (MuseProtocolException error) when (error.Code is -32601 or -32602) { return Parse(identity, await Call("usage/read", new()), false); }
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited)
            {
                try { using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(1)); await process.WaitForExitAsync(grace.Token); }
                catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); }
            }
            try { await errors.WaitAsync(TimeSpan.FromSeconds(1)); } catch (Exception) { }
        }
    }
    private sealed class MuseProtocolException(int code) : Exception("Muse-Protokollaufruf nicht verfügbar.") { internal int Code => code; }
    public void Dispose() { }
}
