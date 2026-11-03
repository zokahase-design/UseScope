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
            foreach (var theme in new[] { "Hell", "Dark" })
            {
                preferences.Theme = theme;
                var widget = new Widget(preferences, data); widget.Update(); widget.Show();
                Dispatcher.UIThread.RunJobs();
                using var image = widget.CaptureRenderedFrame() ?? throw new InvalidOperationException("Kein virtuelles Bild erzeugt.");
                image.Save(Path.Combine(destination, "mac-" + theme + ".png"));
                widget.Close(); widget.DisposeImages();
            }
            return 0;
        }
        finally { foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file); Directory.Delete(root); }
    }
}
