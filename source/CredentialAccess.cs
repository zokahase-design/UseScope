using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class CredentialAccess
{
    internal static Task<JsonObject?> Claude(string profile, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var path = Path.Combine(profile, ".credentials.json");
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
        return Task.FromResult(root?["claudeAiOauth"] as JsonObject);
    }
}
