using System.Text.Json;

namespace UseScope.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (arguments.Length == 2 && arguments[0] == "--check") return Verification.Run(Path.GetFullPath(arguments[1]));
        if (arguments.Length == 2 && arguments[0] == "--export-icon")
        {
            using var icon = Artwork.CreateIcon(); using var file = File.Create(arguments[1]); icon.Save(file); return 0;
        }
        if (arguments.Length == 2 && arguments[0] == "--probe") return Probe(arguments[1]).GetAwaiter().GetResult();
        using var single = new Mutex(true, @"Local\UseScope.Desktop.Application", out var acquired);
        if (!acquired) { MessageBox.Show("UseScope läuft bereits. Bitte das Tray-Symbol verwenden.", "UseScope"); return 0; }
        try
        {
            var preferences = new Preferences(Path.Combine(Preferences.DataDirectory, "settings.json"));
            Application.Run(new DesktopApp(preferences));
            return 0;
        }
        catch (Exception)
        {
            MessageBox.Show("UseScope konnte nicht gestartet werden. Bitte den vollständigen Programmordner und die Einstellungsdatei prüfen. Bestehende Daten wurden nicht zurückgesetzt.", "UseScope", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally { single.ReleaseMutex(); }
    }
    private static async Task<int> Probe(string path)
    {
        var preferences = new Preferences(Path.Combine(Preferences.DataDirectory, "settings.json"));
        var outcomes = new List<object>();
        var failed = false;
        foreach (var identity in preferences.Identities.Where(identity => identity.Enabled))
        {
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.Portal)
            { outcomes.Add(new { Vendor = identity.Vendor, Skipped = true, Reason = "PortalOnly" }); continue; }
            using IAccountSource source = identity.Vendor switch { "codex" => new CodexSource(), "claude" => new ClaudeSource(), "nous" => new NousSource(), _ => new ApiAccountSource() };
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            try
            {
                var result = await source.Query(identity, cancellation.Token);
                outcomes.Add(new { Vendor = identity.Vendor, Ok = !result.Failed, Windows = result.Periods.Count, BalanceAvailable = result.Balance is not null, HasObservation = result.Observed is not null });
                failed |= result.Failed;
            }
            catch (Exception exception)
            {
                outcomes.Add(new { Vendor = identity.Vendor, Ok = false, ErrorType = exception.GetType().Name });
                failed = true;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { Passed = !failed, Accounts = outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        return failed ? 1 : 0;
    }
}
