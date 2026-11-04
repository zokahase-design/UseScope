using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace UseScope.Desktop;

internal static class OfflinePreview
{
    internal static int Render(string destination)
    {
        var root = Path.Combine(Path.GetTempPath(), "usescope-mac-render-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Directory.CreateDirectory(destination);
        MacApp.OfflineRender = true;
        AppBuilder.Configure<MacApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia().SetupWithoutStarting();
        try
        {
            var preferences = new Preferences(Path.Combine(root, "settings.json"));
            var claude = preferences.Add("claude", "mira@atelier.example"); var codex = preferences.Add("codex", "noah@studio.example");
            var data = new Dictionary<string, Reading> {
                [claude.Key] = new(claude.Key, [new("5h", 33, DateTimeOffset.Now.AddHours(4)), new("7d", 5, DateTimeOffset.Now.AddDays(2))], "13,87 €", "Verbunden", DateTimeOffset.Now),
                [codex.Key] = new(codex.Key, [new("5h", 83, DateTimeOffset.Now.AddHours(3)), new("7d", 62, DateTimeOffset.Now.AddDays(1))], "Credits 12,50", "Verbunden", DateTimeOffset.Now)
            };
            var month = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            data[claude.Key] = data[claude.Key] with { Tokens = new(12345, month, DateTimeOffset.UtcNow, "Lokaler Testdatensatz") };
            data[codex.Key] = data[codex.Key] with { Tokens = new(1234567, month, DateTimeOffset.UtcNow, "Lokaler Testdatensatz") };
            foreach (var theme in new[] { "Hell", "Dark" })
            {
                preferences.Theme = theme;
                var widget = new Widget(preferences, data); widget.Update(); widget.Show();
                Dispatcher.UIThread.RunJobs();
                var addRequests = 0; var pinRequests = 0;
                widget.Add += () => addRequests++; widget.Pin += () => pinRequests++;
                widget.AddButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                if (addRequests != 1 || pinRequests != 0) throw new InvalidOperationException("Plus-Schaltfläche startet den falschen Vorgang.");
                using var image = widget.CaptureRenderedFrame() ?? throw new InvalidOperationException("Kein virtuelles Bild erzeugt.");
                image.Save(Path.Combine(destination, "mac-" + theme + ".png"));
                preferences.ShowRemainingQuota = true; widget.Update(); Dispatcher.UIThread.RunJobs();
                using (var remaining = widget.CaptureRenderedFrame() ?? throw new InvalidOperationException("Kein virtuelles Restkontingent-Bild erzeugt."))
                    remaining.Save(Path.Combine(destination, "mac-remaining-" + theme + ".png"));
                preferences.ShowRemainingQuota = false;
                widget.Close(); widget.DisposeImages();
            }
            File.WriteAllText(Path.Combine(destination, "mac-widget-checks.json"), "{\"Passed\":true,\"AddActionBothThemes\":true,\"NativeWindowStarted\":false}");
            return 0;
        }
        finally { foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
