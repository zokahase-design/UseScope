using Avalonia;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Drawing;
using Avalonia.Headless;
using Avalonia.Native;

namespace UseScope.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--check") return MacChecks.Run(args[1]);
        if (args.Length == 2 && args[0] == "--render") return OfflinePreview.Render(args[1]);
        if (!OperatingSystem.IsMacOS()) return 2;
        using var instance = new Mutex(true, "UseScope.Mac.Desktop.Instance", out var acquired);
        if (!acquired) return 0;
        try { return AppBuilder.Configure<MacApp>().UseAvaloniaNative().UseSkia().StartWithClassicDesktopLifetime(args); }
        finally { instance.ReleaseMutex(); }
    }
}

internal static class MacChecks
{
    internal static int Run(string destination)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        var temporary = Path.Combine(Path.GetTempPath(), "usescope-mac-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary); Directory.CreateDirectory(destination);
        try
        {
            ApiChecks.Run(Check).GetAwaiter().GetResult();
            BroadAdapterChecks.Run(Check).GetAwaiter().GetResult();
            MeterChecks.Run(Check).GetAwaiter().GetResult();
            TokenChecks.Run(Check).GetAwaiter().GetResult();
            TokenPeriodChecks.Run(Check).GetAwaiter().GetResult();
            PollingChecks.Run(Check).GetAwaiter().GetResult();
            NativeUsageChecks.Run(Check).GetAwaiter().GetResult();
            if (OperatingSystem.IsMacOS())
            {
                var unknown = new Identity("offline-check-" + Guid.NewGuid().ToString("N"), "deepseek", "Offline", temporary, true, true, 60, false);
                Check(SecretVault.Read(unknown, default).GetAwaiter().GetResult() is null, "Native Keychain query without UI for a new test-only account");
            }
            var path = Path.Combine(temporary, "settings.json"); var preferences = new Preferences(path);
            Check(preferences.Identities.Count == 0, "Fresh install starts without accounts");
            var a = preferences.Add("claude", "Beispiel"); var b = preferences.Add("codex", "Demo");
            Check(a.Profile != b.Profile, "Separate account directories");
            preferences.Reorder(b.Key, a.Key); Check(new Preferences(path).Identities[0].Key == b.Key, "Ordering persists");
            preferences.Theme = "Dark"; preferences.Save(); Check(new Preferences(path).Theme == "Dark", "Theme persists");
            preferences.Remove(a); preferences.Remove(b); Check(new Preferences(path).Identities.Count == 0, "No deleted accounts restored");
            var wallet = JsonNode.Parse("{\"balance\":{\"money\":{\"amount_minor\":646,\"currency\":\"EUR\",\"exponent\":2}}}")!.AsObject();
            Check(Limits.Wallet(wallet) == "6,46 €", "Money preserves reported unit and exponent");
            Check(Limits.Wallet(JsonNode.Parse("{\"amount\":100,\"currency\":\"EUR\"}")!.AsObject()) is null, "Unknown amount units rejected");
            Check(Platform.KeychainService("/tmp/profile-a") != Platform.KeychainService("/tmp/profile-b"), "Keychain slot scoped per selected profile");
            Check(Platform.ShellQuote("a'b") == "'a'\\''b'", "Terminal arguments shell-escaped");
            var limits = Limits.Anthropic(JsonNode.Parse("{\"five_hour\":{\"utilization\":33},\"seven_day\":{\"utilization\":62}}")!.AsObject(), false);
            Check(limits.Count == 2 && limits[0].Percent == 33, "Shared provider mapping");
            File.WriteAllText(Path.Combine(destination, "mac-checks.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks, InteractiveUiTested = false }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(destination, "mac-checks.json"), JsonSerializer.Serialize(new { Passed = false, Checks = checks, Error = error.ToString() })); return 1;
        }
        finally { foreach (var file in Directory.EnumerateFiles(temporary)) File.Delete(file); Directory.Delete(temporary); }
    }
}
