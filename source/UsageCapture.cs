using System.Text;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

// Retains at most a bounded response fragment in memory, never request/prompt text on disk.
internal sealed class UsageCapture(string provider, bool eventStream)
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _buffer = new();
    private readonly StringBuilder _event = new();
    private bool _oversized;
    private long? _reportedTotal;
    internal long? Input, Output, Total;
    internal decimal? Cost;
    internal string? Currency, Model, ResponseId;
    internal void Feed(ReadOnlySpan<byte> bytes)
    {
        if (_oversized) return;
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)]; var length = _decoder.GetChars(bytes, chars, false);
        _buffer.Append(chars, 0, length);
        if (eventStream)
        {
            var text = _buffer.ToString(); int begin = 0, end;
            while ((end = text.IndexOf('\n', begin)) >= 0)
            {
                var line = text[begin..end].TrimEnd('\r'); begin = end + 1;
                EventLine(line);
            }
            if (begin > 0) _buffer.Remove(0, begin);
        }
        if (_buffer.Length > 2_000_000 || _event.Length > 2_000_000) { _buffer.Clear(); _event.Clear(); _oversized = true; }
    }
    private void EventLine(string line)
    {
        if (line.Length == 0) { if (_event.Length > 0) Parse(_event.ToString()); _event.Clear(); return; }
        if (line.StartsWith("data:", StringComparison.Ordinal)) { if (_event.Length > 0) _event.Append('\n'); _event.Append(line[5..].TrimStart(' ')); }
    }
    internal void Finish()
    {
        if (!_oversized && _buffer.Length > 0)
        { var text = _buffer.ToString(); if (eventStream) EventLine(text.TrimEnd('\r')); else Parse(text); }
        if (!_oversized && eventStream && _event.Length > 0) Parse(_event.ToString());
        _buffer.Clear(); _event.Clear();
    }
    private static long? Count(JsonNode? value)
    {
        if (value is null) return null;
        try { return MeterData.Count(value); } catch (Exception error) when (error is InvalidDataException or OverflowException) { return null; }
    }
    private static long? Max(long? old, long? value) => value is null ? old : old is null ? value : Math.Max(old.Value, value.Value);
    private static long? Add(params long?[] values) => values.Any(value => value is null) ? null : values.Aggregate(0L, (sum, value) => checked(sum + value!.Value));
    private void Parse(string text)
    {
        if (text.Length == 0 || text == "[DONE]") return;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root) return;
            Model ??= Identifier(JsonData.Text(root["model"]) ?? JsonData.Text(root["response"]?["model"]) ?? JsonData.Text(root["message"]?["model"]));
            ResponseId ??= Safe(JsonData.Text(root["id"]) ?? JsonData.Text(root["response"]?["id"]) ?? JsonData.Text(root["message"]?["id"]));
            var usage = root["usage"] as JsonObject ?? root["response"]?["usage"] as JsonObject ?? root["message"]?["usage"] as JsonObject ?? root["delta"]?["usage"] as JsonObject;
            if (usage is not null)
            {
                var incoming = Count(usage["prompt_tokens"]) ?? Count(usage["input_tokens"]);
                var outgoing = Count(usage["completion_tokens"]) ?? Count(usage["output_tokens"]);
                if (usage["tokens"] is JsonObject cohere) { incoming = Count(cohere["input_tokens"]); outgoing = Count(cohere["output_tokens"]); }
                // Anthropic input_tokens excludes cache read/creation. OpenAI input tokens already include cache.
                if (usage["cache_read_input_tokens"] is not null || usage["cache_creation_input_tokens"] is not null)
                {
                    if (incoming is not null) incoming = Add(incoming, Count(usage["cache_read_input_tokens"]) ?? 0, Count(usage["cache_creation_input_tokens"]) ?? 0);
                }
                Input = Max(Input, incoming); Output = Max(Output, outgoing); _reportedTotal = Max(_reportedTotal, Count(usage["total_tokens"]));
                if (provider == "openrouter" && usage["cost"] is not null)
                { var cost = MeterData.Decimal(usage["cost"]); if (cost >= 0) { Cost = cost; Currency = "USD"; } }
                if (provider == "xai" && usage["cost_in_usd_ticks"] is not null)
                { var ticks = MeterData.Count(usage["cost_in_usd_ticks"]); Cost = ticks / 10_000_000_000m; Currency = "USD"; }
            }
            if (root["usageMetadata"] is JsonObject gemini)
            {
                Input = Max(Input, Count(gemini["promptTokenCount"]));
                Output = Max(Output, Add(Count(gemini["candidatesTokenCount"]), Count(gemini["thoughtsTokenCount"]) ?? 0));
                _reportedTotal = Max(_reportedTotal, Count(gemini["totalTokenCount"]));
            }
            Total = _reportedTotal ?? (Input is not null && Output is not null ? Add(Input, Output) : null);
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or InvalidDataException or OverflowException)
        { /* Forwarding succeeds even when usage metadata is absent or unsupported. */ }
    }
    private static string? Identifier(string? value) => value is { Length: > 0 and <= 200 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || "_-./:@+".Contains(character)) ? value : null;
    private static string? Safe(string? value) => value is { Length: > 0 and <= 200 } && !value.Any(char.IsControl) ? value : null;
}
