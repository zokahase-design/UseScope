using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class Verification
{
    internal static int Run(string destination)
    {
        Directory.CreateDirectory(destination);
        var temporary = Path.Combine(Path.GetTempPath(), "usescope-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var checks = new List<string>();
        void Check(bool success, string name) { if (!success) throw new InvalidOperationException(name); checks.Add(name); }
        try
        {
            ApiChecks.Run(Check).GetAwaiter().GetResult();
            var apiProfile = Path.Combine(temporary, "protected-api");
            var apiAccount = new Identity("protected-example", "deepseek", "Beispiel", apiProfile, true, true, 60, false);
            SecretVault.Delete(apiAccount, default).GetAwaiter().GetResult();
            Check(!Directory.Exists(apiProfile), "An API account cancelled before first key can be removed");
            SecretVault.Write(apiAccount, "example-test-key", default).GetAwaiter().GetResult();
            Check(SecretVault.Read(apiAccount, default).GetAwaiter().GetResult() == "example-test-key", "Windows DPAPI API key roundtrip");
            Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(apiProfile, "api-key.dpapi"))).Contains("example-test-key"), "API credential never stored as plaintext");
            var accountBound = false;
            try { SecretVault.Read(apiAccount with { Key = "different-example" }, default).GetAwaiter().GetResult(); }
            catch (System.ComponentModel.Win32Exception) { accountBound = true; }
            Check(accountBound, "Windows API credential is bound to its selected identity");
            SecretVault.Delete(apiAccount, default).GetAwaiter().GetResult();
            Check(SecretVault.Read(apiAccount, default).GetAwaiter().GetResult() is null, "Disconnect removes the local API credential");
            Directory.Delete(apiProfile);
            MonitorChecks(Check).GetAwaiter().GetResult();
            Check(JsonData.Number(JsonValue.Create("NaN")) is null, "Nonfinite numbers rejected");
            Check(JsonData.Time(JsonValue.Create(double.MaxValue)) is null, "Invalid reset epoch rejected");
            var clamped = JsonData.Limit("Test", JsonNode.Parse("{\"p\":120}"), "p", "r");
            Check(clamped?.Percent == 100 && clamped.Reset is null, "Out-of-range usage clamped without inventing reset");
            var codex = Limits.OpenAI("test", JsonNode.Parse("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":{\"usedPercent\":24,\"windowDurationMins\":300,\"resetsAt\":1900000000},\"secondary\":{\"usedPercent\":39,\"windowDurationMins\":10080},\"credits\":{\"balance\":\"5.5\",\"hasCredits\":true,\"unlimited\":false}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":24}},\"spark\":{\"limitName\":\"Spark\",\"primary\":{\"usedPercent\":45,\"windowDurationMins\":300}}}}")!, null);
            Check(codex.Periods.Count == 2 && codex.Periods[0].Caption == "5h" && codex.Periods[1].Caption == "7d", "Codex main windows respect hidden model preference");
            Check(codex.Balance == "Credits 5.5", "Codex reported credits retained in their own unit");
            var claude = JsonNode.Parse("{\"five_hour\":{\"utilization\":25,\"resets_at\":\"2030-01-01T00:00:00Z\"},\"seven_day\":{\"utilization\":50},\"seven_day_opus\":{\"utilization\":12},\"extra_usage\":{\"monthly_limit\":2000,\"used_credits\":100}}") as JsonObject;
            Check(Limits.Anthropic(claude!, false).Count == 2 && Limits.Anthropic(claude!, true).Count == 3, "Claude optional model windows respect preference");
            Check(Limits.Wallet(claude!) is null, "Monthly cap minus spend never becomes wallet credit");
            Check(Limits.Wallet(JsonNode.Parse("{\"balance\":{\"money\":{\"amount_minor\":646,\"currency\":\"EUR\",\"exponent\":2}}}")!.AsObject()) == "6,46 €", "Wallet money uses provider currency and exponent");
            Check(Limits.Wallet(JsonNode.Parse("{\"balance\":{\"money\":{\"amount_minor\":0,\"currency\":\"EUR\",\"exponent\":2}}}")!.AsObject()) == "0,00 €", "Zero balance is known, not missing");
            Check(Limits.Wallet(JsonNode.Parse("{\"amount\":646,\"currency\":\"EUR\"}")!.AsObject()) is null, "An unspecified amount unit is never assumed to be cents");
            Check(Limits.Wallet(JsonNode.Parse("{\"amount\":646}")!.AsObject()) is null, "Wallet without a reported currency stays unknown");
            var path = Path.Combine(temporary, "settings.json");
            File.WriteAllText(path, "{\"ColorTheme\":\"Glas\",\"AlwaysOnTop\":true,\"Accounts\":[{\"Id\":\"a\",\"Provider\":\"codex\",\"DisplayName\":\"Example\"},{\"Id\":\"b\",\"Provider\":\"claude\",\"DisplayName\":\"Demo\"}],\"AccountOrder\":[\"b\",\"a\"],\"Providers\":{},\"UnrelatedPreference\":17}");
            var before = File.ReadAllText(path);
            var preferences = new Preferences(path);
            Check(File.ReadAllText(path) == before, "Loading existing settings does not modify them");
            Check(preferences.Theme == "Hell" && preferences.Identities[0].Key == "b", "Old theme fallback and account order preserved");
            preferences.SetBalance(preferences.Identities[0], false);
            Check(JsonNode.Parse(File.ReadAllText(path))!["UnrelatedPreference"]!.GetValue<int>() == 17, "Unknown settings survive mutation");
            preferences.Move(preferences.Identities[0], 1);
            Check(preferences.Identities[0].Key == "a", "Account reordering persists");
            preferences.Reorder("b", "a");
            Check(new Preferences(path).Identities[0].Key == "b", "Widget drag order persists without changing account profiles");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var failedSave = false;
                try { preferences.Reorder("a", "b"); } catch (IOException) { failedSave = true; }
                Check(failedSave && preferences.Identities[0].Key == "b", "A failed drag-order save rolls back the in-memory order");
            }
            var added = preferences.Add("nous", "Example Nous");
            Check(preferences.Identities.Count == 3 && Path.GetFileName(added.Profile) == added.Key, "New account gets an isolated profile");
            preferences.Remove(added);
            foreach (var identity in preferences.Identities.ToArray()) preferences.Remove(identity);
            Check(new Preferences(path).Identities.Count == 0, "Removing the last account never restores defaults");
            File.WriteAllText(path, "{broken");
            try { _ = new Preferences(path); Check(false, "Invalid settings must fail"); } catch (JsonException) { }
            Check(File.ReadAllText(path) == "{broken", "Malformed settings are not silently overwritten");
            var area = new Rectangle(-1920, 0, 1920, 1040);
            var size = new Size(400, 160);
            var position = Platform.Fit(new Point(100, 2000), size, area);
            Check(area.Contains(new Rectangle(position, size)), "Placement stays within a monitor with negative coordinates");
            Check(position.Y + size.Height == area.Bottom - 16 && position.X + size.Width == area.Right - 16,
                "Dragging below the work area restores a 16px taskbar and screen-edge gap");
            var roomy = new Rectangle(0, 0, 1920, 1040);
            Check(Platform.Fit(new Point(400, 400), size, roomy) == new Point(400, 400), "Interior placement remains unchanged");
            var oversized = Platform.Fit(new Point(900, 900), new Size(500, 300), new Rectangle(0, 0, 200, 100));
            Check(oversized == Point.Empty, "Small work areas do not produce invalid clamping bounds");
            var uiPath = Path.Combine(temporary, "ui.json");
            var uiPreferences = new Preferences(uiPath);
            var first = uiPreferences.Add("claude", "mira@atelier.example");
            var second = uiPreferences.Add("codex", "noah@studio.example");
            var third = uiPreferences.Add("claude", "noah@studio.example");
            var fourth = uiPreferences.Add("claude", "leo@werkraum.example");
            var fifth = uiPreferences.Add("codex", "mira@atelier.example");
            var readings = new Dictionary<string, Reading> {
                [first.Key] = new(first.Key, [new("5h", 33, DateTimeOffset.Now.AddHours(4).AddMinutes(24)), new("7d", 5, DateTimeOffset.Now.AddDays(2).AddHours(2).AddMinutes(4))], "—", "Verbunden", DateTimeOffset.Now),
                [second.Key] = new(second.Key, [new("5h", 83, DateTimeOffset.Now.AddHours(4).AddMinutes(14)), new("7d", 13, DateTimeOffset.Now.AddDays(6).AddHours(23))], "Credits 0,00", "Verbunden", DateTimeOffset.Now),
                [third.Key] = new(third.Key, [new("5h", 0, DateTimeOffset.Now.AddHours(5)), new("7d", 0, DateTimeOffset.Now.AddDays(5).AddHours(13))], "10,00 €", "Verbunden", DateTimeOffset.Now),
                [fourth.Key] = new(fourth.Key, [new("5h", 0, DateTimeOffset.Now.AddHours(5)), new("7d", 94, DateTimeOffset.Now.AddDays(1).AddHours(20))], "13,87 €", "Verbunden", DateTimeOffset.Now),
                [fifth.Key] = new(fifth.Key, [new("5h", 0, DateTimeOffset.Now.AddHours(5)), new("7d", 62, DateTimeOffset.Now.AddDays(1).AddHours(20))], "Credits 0,00", "Verbunden", DateTimeOffset.Now)
            };
            foreach (var theme in new[] { "Hell", "Dark" })
            {
                uiPreferences.Theme = theme; uiPreferences.Pinned = true;
                using var dashboard = new Dashboard(uiPreferences);
                dashboard.Render(readings);
                dashboard.MenuActive(true);
                Check(!dashboard.TopMost && uiPreferences.Pinned, theme + ": menu suspends foreground priority without changing preference");
                dashboard.Render(readings);
                Check(!dashboard.TopMost, theme + ": data refresh cannot restore foreground priority over a menu");
                dashboard.MenuActive(false);
                Check(dashboard.TopMost, theme + ": foreground priority restored on menu close");
                uiPreferences.Pinned = false; dashboard.MenuActive(true); dashboard.MenuActive(false);
                Check(!dashboard.TopMost, theme + ": unpinned preference preserved");
                Check(dashboard.Opacity == 1, theme + ": opaque widget");
                Check(dashboard.Font.Unit == GraphicsUnit.Pixel, theme + ": compact geometry uses DPI-independent pixel fonts");
                using (var logo = Artwork.ProviderImage("codex", theme) as Bitmap)
                {
                    Check(logo is not null, theme + ": existing OpenAI logo is packaged");
                    double brightness = 0; int count = 0;
                    for (var y = 0; y < logo!.Height; y++)
                        for (var x = 0; x < logo.Width; x++) { var pixel = logo.GetPixel(x, y); if (pixel.A > 200) { brightness += (pixel.R + pixel.G + pixel.B) / 3d; count++; } }
                    Check(count > 0 && (theme == "Hell" ? brightness / count < 60 : brightness / count > 190), theme + ": restored OpenAI logo contrasts with background");
                }
                _ = dashboard.Handle; dashboard.PerformLayout();
                using var bitmap = Snapshot(dashboard);
                bitmap.Save(Path.Combine(destination, "widget-" + theme + ".png"), ImageFormat.Png);
                var inkPixels = 0;
                for (var y = 0; y < bitmap.Height; y++)
                    for (var x = 0; x < bitmap.Width; x++)
                    { var pixel = bitmap.GetPixel(x, y); var ink = Colors.For(theme).Ink;
                        if (Math.Abs(pixel.R - ink.R) + Math.Abs(pixel.G - ink.G) + Math.Abs(pixel.B - ink.B) < 60) inkPixels++; }
                Check(inkPixels > 100, theme + ": offscreen render contains labels and controls");
                using var menu = new ContextMenuStrip(); var nested = new ToolStripMenuItem("Konten"); nested.DropDownItems.Add("Beispielkonto"); menu.Items.Add(nested);
                DesktopApp.StyleMenu(menu, Colors.For(theme));
                Check(nested.DropDown.BackColor == menu.BackColor && menu.ForeColor == Colors.For(theme).Ink, theme + ": nested menu palette matches contrast");
            }
            var apiPreview = new Preferences(Path.Combine(temporary, "api-preview.json"));
            var apiReadings = new Dictionary<string, Reading>();
            foreach (var vendor in new[] { "deepseek", "moonshot", "openrouter", "openai-api", "xai", "gemini", "qwen", "wan", "minimax", "meta" })
            {
                var account = apiPreview.Add(vendor, Providers.Get(vendor).Name);
                apiReadings[account.Key] = vendor switch {
                    "deepseek" => ApiAccountSource.ParseDeepSeek(account, JsonNode.Parse("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":\"18.40\"}]}")!.AsObject()),
                    "moonshot" => ApiAccountSource.ParseMoonshot(account, JsonNode.Parse("{\"status\":true,\"data\":{\"available_balance\":12.5}}")!.AsObject()),
                    "openrouter" => ApiAccountSource.ParseOpenRouter(account, JsonNode.Parse("{\"data\":{\"usage\":23.7,\"usage_monthly\":4.2,\"limit_remaining\":6.3}}")!.AsObject()),
                    "xai" => ApiAccountSource.ParseXai(account, JsonNode.Parse("{\"total\":{\"val\":\"-1500\"}}")!.AsObject()),
                    "openai-api" => new(account.Key, [], null, "API verbunden · Organisation", DateTimeOffset.Now, Detail: "Monat 8,90 USD · 15.900 Text-Token\nText-Input 12.500 Token · Output 3.400 Token"),
                    _ => new(account.Key, [], null, "Portal · keine automatische Messung", null, Detail: Providers.Get(vendor).Help)
                };
            }
            foreach (var theme in new[] { "Hell", "Dark" })
            {
                apiPreview.Theme = theme;
                foreach (var provider in Providers.All.Where(provider => Providers.Icon(provider.Id, theme == "Dark") is not null))
                { using var icon = Artwork.ProviderImage(provider.Id, theme); Check(icon is not null, theme + ": logo available for " + provider.Id); }
                using var dashboard = new Dashboard(apiPreview); dashboard.Render(apiReadings);
                using var bitmap = Snapshot(dashboard); bitmap.Save(Path.Combine(destination, "api-" + theme + ".png"), ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(destination, "checks.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(destination, "checks.json"), JsonSerializer.Serialize(new { Passed = false, Checks = checks, Error = exception.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            return 1;
        }
        finally
        {
            // Only test-owned files, outside any credential or application data folder.
            foreach (var file in Directory.EnumerateFiles(temporary)) File.Delete(file);
            Directory.Delete(temporary);
        }
    }

    private static Bitmap Snapshot(Control control)
    {
        _ = control.Handle;
        control.PerformLayout();
        var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        // Invisible forms have effectively invisible children. Composite their
        // actual controls explicitly; never show or activate a test window.
        using var graphics = Graphics.FromImage(bitmap);
        foreach (var child in control.Controls.Cast<Control>().Reverse())
        {
            if (child.Width <= 0 || child.Height <= 0) continue;
            using var layer = Snapshot(child);
            graphics.DrawImageUnscaled(layer, child.Left, child.Top);
        }
        return bitmap;
    }

    private sealed class ControlledSource : IAccountSource
    {
        internal int Calls;
        internal bool Pause;
        internal bool RateLimit;
        internal bool Disposed;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Reading> Query(Identity identity, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Calls); Entered.TrySetResult();
            if (Pause) await Release.Task.WaitAsync(cancellation);
            if (RateLimit) throw new RemoteFailure("Warten", DateTimeOffset.Now.AddMinutes(3));
            return new(identity.Key, [], null, "OK", DateTimeOffset.Now);
        }
        public void Dispose() { Disposed = true; }
    }
    private static async Task MonitorChecks(Action<bool, string> check)
    {
        var identity = new Identity("test", "codex", "Example", "unused", true, true, 60, false);
        var source = new ControlledSource { Pause = true };
        var replacement = new ControlledSource();
        var created = 0;
        using (var monitor = new AccountMonitor(_ => ++created == 1 ? source : replacement))
        {
            var reports = new System.Collections.Concurrent.ConcurrentBag<Reading>();
            monitor.Changed += reports.Add;
            var query = monitor.Refresh([identity], true);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var authenticated = false;
            var authentication = monitor.Authenticate(identity, _ => { authenticated = true; return Task.CompletedTask; });
            check(!authenticated, "Authentication waits for the account's active query");
            source.Release.TrySetResult();
            await Task.WhenAll(query, authentication);
            check(authenticated && reports.Count == 0, "An earlier query cannot publish a result after an authentication change");
            check(source.Disposed && created == 2, "Authentication discards provider caches from the previous account");
            source.Pause = false;
            await monitor.Refresh([identity], true);
            check(reports.Count == 1 && monitor.IsCurrent(reports.Single()), "New account epoch accepts subsequent query results");
        }
        var limited = new ControlledSource { RateLimit = true };
        using (var monitor = new AccountMonitor(_ => limited))
        {
            await monitor.Refresh([identity], true);
            await monitor.Refresh([identity], true);
            check(limited.Calls == 1, "Manual refresh cannot bypass server Retry-After");
        }
        check(!new RemoteFailure("Server failure").Authentication && new RemoteFailure("Login", authentication: true).Authentication,
            "Transport distinguishes authentication errors from provider outages");
    }
}
