using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class GoogleProjectSource : IAccountSource
{
    private readonly BillingTransport _http = new();
    private readonly Func<HttpRequestMessage, CancellationToken, Task<JsonObject>> _send;
    private readonly Func<Identity, CancellationToken, Task<string>> _token;
    private string? _accessToken;
    private DateTimeOffset _tokenUntil, _costUntil;
    private string? _cost;
    internal GoogleProjectSource(Func<HttpRequestMessage, CancellationToken, Task<JsonObject>>? send = null, Func<Identity, CancellationToken, Task<string>>? token = null)
    { _send = send ?? _http.Send; _token = token ?? ((account, cancellation) => NativeConnections.Read(account, cancellation, "auth", "print-access-token", "--quiet")); }
    public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
    {
        var project = JsonData.Text(identity.ConnectionOptions?["ProjectId"]) ?? ""; var table = JsonData.Text(identity.ConnectionOptions?["BillingExport"]) ?? "";
        if (project.Length == 0) return new(identity.Key, [], null, "Google-Projekt konfigurieren", null, true);
        ConnectionSetup.Google(project, table);
        if (_accessToken is null || _tokenUntil <= DateTimeOffset.UtcNow)
        { _accessToken = Providers.ValidateKey(await _token(identity, cancellation)); _tokenUntil = DateTimeOffset.UtcNow.AddMinutes(45); }
        var now = DateTimeOffset.UtcNow; var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        if (end <= start) return new(identity.Key, [], null, "Noch kein abgeschlossenes UTC-Stundenfenster", null);
        try
        {
            var requests = await Metric(project, "metric.type=\"serviceruntime.googleapis.com/api/request_count\" AND resource.type=\"consumed_api\" AND resource.labels.service=\"generativelanguage.googleapis.com\"", start, end, cancellation);
            var output = await Metric(project, "metric.type=\"generativelanguage.googleapis.com/generate_content_usage_output_token_count\"", start, end, cancellation);
            if (table.Length > 0 && _costUntil <= now)
            {
                _costUntil = now.AddMinutes(30); _cost = "Projektkosten derzeit nicht verfügbar; Exporttabelle und Leserechte prüfen";
                try { _cost = await Cost(project, table, start, end, cancellation); }
                catch (RemoteFailure error) when (error.Retry is null) { }
                catch (Exception error) when (error is HttpRequestException or InvalidDataException) { }
            }
            var countText = requests.Count is long count ? count.ToString("N0", ApiAccountSource.German) + " Anfragen" : "keine Anfragen gemeldet";
            var observed = requests.Observed is null ? output.Observed : output.Observed is null ? requests.Observed : requests.Observed < output.Observed ? requests.Observed : output.Observed;
            return new(identity.Key, [], null, observed is null ? "Projekt verbunden · keine Messpunkte" : "Projekt verbunden · Google Monitoring", observed,
                Detail: "Monat · " + countText + "\nProjekt " + project + " · Gemini API, keine Gemini-App-Aboquote. Anfragen können Fehlerantworten enthalten; Monitoring kann verzögert sein."
                + "\n" + (table.Length == 0 ? "Kosten nicht konfiguriert: optional bestehende Cloud-Billing-Exporttabelle angeben." : _cost + " · gesamtes Projekt, keine reine Gemini-Rechnung; Kostenabruf maximal alle 30 Minuten.")
                + "\nZeitraum: " + start.ToString("u") + " bis " + end.ToString("u") + " (abgeschlossene UTC-Stunden)",
                Tokens: output.Count is long outputTokens ? new(outputTokens, start, end, "Google-Projekt · nur gemeldete Output-Token der Gemini API; Input fehlt. Abgeschlossene UTC-Stunden, Monitoring-Verzögerung möglich.", Output: outputTokens, Partial: true) : null);
        }
        catch (RemoteFailure failure) when (failure.Authentication) { _accessToken = null; throw; }
    }
    private HttpRequestMessage Request(HttpMethod method, string url, string project)
    {
        var request = new HttpRequestMessage(method, url); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Headers.Add("x-goog-user-project", project); return request;
    }
    private async Task<(long? Count, DateTimeOffset? Observed)> Metric(string project, string filter, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellation)
    {
        var url = "https://monitoring.googleapis.com/v3/projects/" + Uri.EscapeDataString(project) + "/timeSeries?filter=" + Uri.EscapeDataString(filter)
            + "&interval.startTime=" + Uri.EscapeDataString(start.ToString("O")) + "&interval.endTime=" + Uri.EscapeDataString(end.ToString("O"))
            + "&view=FULL&aggregation.alignmentPeriod=3600s&aggregation.perSeriesAligner=ALIGN_SUM&aggregation.crossSeriesReducer=REDUCE_SUM&pageSize=1000";
        var seen = new HashSet<string>(); string? cursor = null; long? count = null; DateTimeOffset? observed = null;
        for (var index = 0; index < 32; index++)
        {
            var response = await _send(Request(HttpMethod.Get, url + (cursor is null ? "" : "&pageToken=" + Uri.EscapeDataString(cursor)), project), cancellation);
            if (response["executionErrors"] is JsonArray errors && errors.Count > 0) throw new InvalidDataException("Monitoring lieferte nur Teildaten.");
            if (response["timeSeries"] is JsonArray series)
                foreach (var value in series)
                {
                    if (value?["points"] is not JsonArray points) throw new InvalidDataException("Monitoring-Punkte fehlen.");
                    var parsed = ParsePoints(points); if (parsed.Count is long amount) count = checked((count ?? 0) + amount);
                    if (parsed.Observed is DateTimeOffset stamp && (observed is null || stamp > observed)) observed = stamp;
                }
            else if (response["timeSeries"] is not null) throw new InvalidDataException();
            cursor = JsonData.Text(response["nextPageToken"]); if (string.IsNullOrEmpty(cursor)) return (count, observed);
            if (!seen.Add(cursor)) throw new InvalidDataException("Monitoring-Seiten wiederholen sich.");
        }
        throw new InvalidDataException("Monitoring-Abfrage unvollständig.");
    }
    internal static (long? Count, DateTimeOffset? Observed) ParsePoints(JsonArray points)
    {
        long? sum = null; DateTimeOffset? stamp = null;
        foreach (var point in points)
        {
            var value = MeterData.Count(point?["value"]?["int64Value"]); var time = JsonData.Time(point?["interval"]?["endTime"]) ?? throw new InvalidDataException("Messzeitpunkt fehlt.");
            sum = checked((sum ?? 0) + value); if (stamp is null || time > stamp) stamp = time;
        }
        return (sum, stamp);
    }
    internal static JsonObject BillingQuery(string project, string table, DateTimeOffset start, DateTimeOffset end)
    {
        ConnectionSetup.Google(project, table); if (table.Length == 0) throw new ArgumentException();
        JsonObject Parameter(string name, string type, string value) => new() { ["name"] = name, ["parameterType"] = new JsonObject { ["type"] = type }, ["parameterValue"] = new JsonObject { ["value"] = value } };
        return new() {
            ["query"] = "SELECT currency, SUM(cost + IFNULL((SELECT SUM(c.amount) FROM UNNEST(credits) c), 0)) AS net_cost FROM `" + table
                + "` WHERE project.id = @project AND usage_start_time >= @start AND usage_start_time < @end GROUP BY currency",
            ["useLegacySql"] = false, ["parameterMode"] = "NAMED", ["timeoutMs"] = 10000, ["maxResults"] = 100,
            ["maximumBytesBilled"] = "104857600", ["requestId"] = Guid.NewGuid().ToString("N"),
            ["queryParameters"] = new JsonArray(Parameter("project", "STRING", project), Parameter("start", "TIMESTAMP", start.ToString("O")), Parameter("end", "TIMESTAMP", end.ToString("O")))
        };
    }
    private async Task<string> Cost(string project, string table, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellation)
    {
        var request = Request(HttpMethod.Post, "https://bigquery.googleapis.com/bigquery/v2/projects/" + Uri.EscapeDataString(project) + "/queries", project);
        request.Content = new StringContent(BillingQuery(project, table, start, end).ToJsonString(), Encoding.UTF8, "application/json");
        var response = await _send(request, cancellation);
        for (var attempt = 0; !JsonData.Flag(response["jobComplete"]) && attempt < 3; attempt++)
        {
            if (response["errors"] is JsonArray reported && reported.Count > 0) throw new RemoteFailure("Billing-Exportabfrage fehlgeschlagen");
            if (JsonData.Text(response["jobReference"]?["projectId"]) != project || JsonData.Text(response["jobReference"]?["jobId"]) is not string job) throw new InvalidDataException("Billing-Job fehlt.");
            var location = JsonData.Text(response["jobReference"]?["location"]);
            var url = "https://bigquery.googleapis.com/bigquery/v2/projects/" + Uri.EscapeDataString(project) + "/queries/" + Uri.EscapeDataString(job) + "?timeoutMs=10000&maxResults=100";
            if (location is not null) url += "&location=" + Uri.EscapeDataString(location);
            response = await _send(Request(HttpMethod.Get, url, project), cancellation);
        }
        if (response["errors"] is JsonArray errors && errors.Count > 0) throw new RemoteFailure("Billing-Exportabfrage fehlgeschlagen");
        if (!JsonData.Flag(response["jobComplete"])) throw new RemoteFailure("Billing-Exportabfrage noch nicht abgeschlossen; im Anbieterportal prüfen");
        if (!string.IsNullOrEmpty(JsonData.Text(response["pageToken"]))) throw new InvalidDataException("Kostenabfrage lieferte unerwartete weitere Seiten.");
        if (response["rows"] is not JsonArray rows || rows.Count == 0) return "Keine exportierten Projektkosten gemeldet";
        var values = new List<string>();
        foreach (var row in rows)
        {
            if (row?["f"] is not JsonArray fields || fields.Count != 2) throw new InvalidDataException();
            values.Add(MeterData.Money(MeterData.Decimal(fields[1]?["v"]), MeterData.Currency(JsonData.Text(fields[0]?["v"]))));
        }
        return "Exportierte Projektkosten netto: " + string.Join(" · ", values);
    }
    public void Dispose() => _http.Dispose();
}
