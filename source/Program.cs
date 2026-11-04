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
        if (System.Diagnostics.Process.GetProcessesByName("UseScope").Any(process => { using (process) return process.Id != Environment.ProcessId; }))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, "{\"Passed\":false,\"Deferred\":\"UseScopeIsRunning\"}"); return 3;
        }
        var preferences = new Preferences(Path.Combine(Preferences.DataDirectory, "settings.json"));
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<object>();
        var failed = false;
        var accounts = preferences.Identities.Where(identity => identity.Enabled).ToArray();
        using var monitor = new AccountMonitor();
        monitor.Changed += result => {
            var index = Array.FindIndex(accounts, identity => identity.Key == result.Identity);
            outcomes.Add(new { Index = index + 1, Vendor = accounts[index].Vendor, Ok = !result.Failed, Windows = result.Periods.Count,
                BalanceAvailable = result.Balance is not null, HasObservation = result.Observed is not null, TokensAvailable = result.Tokens is not null,
                HttpStatus = result.HttpStatus, Error = result.Failed ? result.Status : null, TokenNote = result.TokenNote, NextAttemptUtc = result.NextAttempt });
            failed |= result.Failed;
        };
        foreach (var identity in accounts)
        {
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.Portal)
            { outcomes.Add(new { Vendor = identity.Vendor, Skipped = true, Reason = "PortalOnly" }); continue; }
            try
            {
                await monitor.Refresh([identity], true);
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
