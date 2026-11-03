using System.Diagnostics;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class DesktopApp : ApplicationContext
{
    private readonly Preferences _preferences;
    private readonly Dashboard _view;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly AccountMonitor _monitor = new();
    private readonly Dictionary<string, Reading> _readings = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 15_000 };
    private readonly ShortcutWindow _shortcut;
    private bool _closing;
    private bool _paintPending;
    internal DesktopApp(Preferences preferences)
    {
        _preferences = preferences;
        _view = new Dashboard(preferences) { ContextMenuStrip = _menu };
        _ = _view.Handle;
        _tray = new NotifyIcon { Text = "UseScope · Nutzungslimits", Icon = Artwork.CreateIcon(), ContextMenuStrip = _menu, Visible = true };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
        _view.OrderChanged += Paint;
        _view.PinRequested += () => Change(() => preferences.Pinned = !preferences.Pinned);
        _menu.Opening += (_, e) => {
            _view.MenuActive(true);
            try { FillMenu(); } catch { e.Cancel = true; _view.MenuActive(false); throw; }
        };
        _menu.Closed += (_, _) => {
            _view.MenuActive(false);
            if (_paintPending) Paint();
        };
        _monitor.Changed += reading => {
            if (!_closing) try { _view.BeginInvoke((Action)(() => {
                if (_monitor.IsCurrent(reading) && preferences.Identities.Any(item => item.Key == reading.Identity)) _readings[reading.Identity] = reading;
                Paint();
            })); } catch (InvalidOperationException) { }
        };
        _shortcut = new ShortcutWindow(preferences.Shortcut, Toggle);
        _clock.Tick += (_, _) => { Paint(); _ = Poll(false); };
        _clock.Start();
        Paint(); _view.Place(); _view.Show();
        _ = Poll(true);
        if (Platform.StartsWithWindows) Platform.StartsWithWindows = true;
    }
    private void Paint()
    {
        if (_closing) return;
        if (_menu.Visible) { _paintPending = true; return; }
        _paintPending = false;
        _view.Render(_readings);
    }
    private async Task Poll(bool force, string? key = null)
    {
        if (_closing) return;
        var identities = _preferences.Identities;
        try { await Task.Run(() => _monitor.Refresh(identities, force, key)); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closing) _tray.Text = "UseScope · Aktualisierung derzeit nicht möglich"; }
    }
    private void Toggle()
    {
        if (_menu.Visible) _menu.Close();
        if (_view.Visible) _view.Hide();
        else { Paint(); _view.Place(); _view.Show(); }
    }
    private void Change(Action mutation)
    {
        try { mutation(); _preferences.Save(); Paint(); }
        catch (Exception) { MessageBox.Show(_view, "Die Änderung konnte nicht gespeichert werden.", "UseScope", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private static ToolStripMenuItem Command(string title, Action action, bool check = false)
    {
        var item = new ToolStripMenuItem(title) { Checked = check };
        item.Click += (_, _) => action();
        return item;
    }
    private void FillMenu()
    {
        var old = _menu.Items.Cast<ToolStripItem>().ToArray();
        _menu.Items.Clear(); foreach (var item in old) { DisposeImages(item); item.Dispose(); }
        _menu.Items.Add(new ToolStripMenuItem("UseScope") { Enabled = false });
        _menu.Items.Add(Command("Widget ein- / ausblenden", Toggle));
        _menu.Items.Add(Command("Nutzung aktualisieren", () => _ = Poll(true)));
        _menu.Items.Add(new ToolStripSeparator());
        var accounts = new ToolStripMenuItem("Konten");
        foreach (var identity in _preferences.Identities)
        {
            var observed = _readings.GetValueOrDefault(identity.Key);
            var title = observed?.Status.Contains('@') == true ? observed.Status : identity.Title;
            var account = new ToolStripMenuItem(title) { Image = Artwork.ProviderImage(identity.Vendor, _preferences.Theme) };
            account.DropDownItems.Add(new ToolStripMenuItem(observed?.Failed == true ? observed.Status : "Kontoverbindung") { Enabled = false });
            var provider = Providers.Get(identity.Vendor);
            account.DropDownItems.Add(Command(provider.Connection == ConnectionKind.Native ? "Anmelden…" : provider.Connection == ConnectionKind.ApiKey ? "API-Schlüssel hinterlegen…" : "Portal öffnen…", () => Login(identity)));
            account.DropDownItems.Add(Command("Offizielles Anbieterportal…", () => OpenPortal(identity)));
            account.DropDownItems.Add(Command("Werte und Verbindung erklären…", () => MessageBox.Show(ReadingPresentation.Detail(identity, _readings.GetValueOrDefault(identity.Key)) + "\n\n" + provider.Help, provider.Name)));
            account.DropDownItems.Add(Command("Verbindung erneuern", () => _ = Poll(true, identity.Key)));
            if (provider.Connection != ConnectionKind.Portal) account.DropDownItems.Add(Command(provider.Connection == ConnectionKind.ApiKey ? "API-Schlüssel lokal löschen…" : "Abmelden…", () => _ = Logout(identity)));
            account.DropDownItems.Add(new ToolStripSeparator());
            account.DropDownItems.Add(Command("Guthaben / Credits anzeigen", () => Change(() => _preferences.SetBalance(identity, !identity.ShowBalance)), identity.ShowBalance));
            account.DropDownItems.Add(Command("Nach oben", () => Change(() => _preferences.Move(identity, -1))));
            account.DropDownItems.Add(Command("Nach unten", () => Change(() => _preferences.Move(identity, 1))));
            account.DropDownItems.Add(Command("Aus Anzeige entfernen…", () => Remove(identity)));
            accounts.DropDownItems.Add(account);
        }
        if (accounts.DropDownItems.Count == 0) accounts.DropDownItems.Add(new ToolStripMenuItem("Noch keine Konten") { Enabled = false });
        _menu.Items.Add(accounts);
        _menu.Items.Add(Command("Konto hinzufügen…", Add));
        var display = new ToolStripMenuItem("Darstellung");
        display.DropDownItems.Add(Command("Immer im Vordergrund", () => Change(() => _preferences.Pinned = !_preferences.Pinned), _preferences.Pinned));
        var themes = new ToolStripMenuItem("Farbschema");
        themes.DropDownItems.Add(Command("Hell", () => Change(() => _preferences.Theme = "Hell"), _preferences.Theme == "Hell"));
        themes.DropDownItems.Add(Command("Dunkel", () => Change(() => _preferences.Theme = "Dark"), _preferences.Theme == "Dark"));
        display.DropDownItems.Add(themes);
        var location = new ToolStripMenuItem("Position");
        foreach (var (key, text) in new[] { ("BottomRight", "Unten rechts"), ("TopRight", "Oben rechts"), ("TopLeft", "Oben links"), ("BottomLeft", "Unten links"), ("Center", "Bildschirmmitte"), ("NearCursor", "Am Mauszeiger"), ("LastPosition", "Letzte Position") })
            location.DropDownItems.Add(Command(text, () => { Change(() => _preferences.Position = key); _view.Place(); }, _preferences.Position == key));
        display.DropDownItems.Add(location);
        _menu.Items.Add(display);
        _menu.Items.Add(Command("Mit Windows starten", () => Change(() => Platform.StartsWithWindows = !Platform.StartsWithWindows), Platform.StartsWithWindows));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Command("UseScope beenden", ExitThread));
        StyleMenu(_menu, Colors.For(_preferences.Theme));
    }
    internal static void StyleMenu(ToolStrip strip, Colors palette)
    {
        strip.BackColor = palette.Surface; strip.ForeColor = palette.Ink;
        strip.Renderer = new ToolStripProfessionalRenderer(new MenuPalette(palette));
        foreach (ToolStripItem item in strip.Items)
        {
            item.ForeColor = palette.Ink;
            if (item is ToolStripMenuItem child && child.HasDropDownItems) StyleMenu(child.DropDown, palette);
        }
    }
    private static void DisposeImages(ToolStripItem item)
    {
        if (item is ToolStripMenuItem child)
            foreach (ToolStripItem descendant in child.DropDownItems) DisposeImages(descendant);
        item.Image?.Dispose(); item.Image = null;
    }
    private sealed class MenuPalette(Colors colors) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => colors.Surface;
        public override Color MenuBorder => colors.Line;
        public override Color ImageMarginGradientBegin => colors.Surface;
        public override Color ImageMarginGradientMiddle => colors.Surface;
        public override Color ImageMarginGradientEnd => colors.Surface;
        public override Color MenuItemSelected => colors.Track;
        public override Color MenuItemBorder => colors.Line;
        public override Color CheckBackground => colors.Track;
        public override Color CheckSelectedBackground => colors.Track;
        public override Color SeparatorDark => colors.Line;
        public override Color SeparatorLight => colors.Surface;
    }
    private void Add()
    {
        using var dialog = new Form { Text = "Konto hinzufügen", ClientSize = new Size(440, 266), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var provider = new ComboBox { Left = 14, Top = 34, Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
        provider.Width = 410; provider.Items.AddRange(Providers.All.Select(item => item.Name).ToArray()); provider.SelectedIndex = 0;
        var hint = new Label { Left = 14, Top = 66, Width = 410, Height = 82, Text = Providers.All[0].Help };
        provider.SelectedIndexChanged += (_, _) => hint.Text = Providers.All[provider.SelectedIndex].Help;
        var name = new TextBox { Left = 14, Top = 174, Width = 410 };
        var add = new Button { Text = "Hinzufügen", Left = 218, Top = 222, Width = 110, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Abbrechen", Left = 334, Top = 222, Width = 90, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([new Label { Text = "Anbieter", Left = 14, Top = 12 }, provider, hint, new Label { Text = "Bezeichnung (optional, z. B. E-Mail oder Projekt)", Left = 14, Top = 152, Width = 410 }, name, add, cancel]);
        dialog.AcceptButton = add; dialog.CancelButton = cancel;
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var vendor = Providers.All[provider.SelectedIndex].Id;
            var title = string.IsNullOrWhiteSpace(name.Text) ? Artwork.VendorName(vendor) + " " + (_preferences.Identities.Count + 1) : name.Text.Trim();
            var identity = _preferences.Add(vendor, title);
            Paint(); Login(identity);
        }
        catch (Exception) { MessageBox.Show("Konto konnte nicht hinzugefügt werden.", "UseScope"); }
    }
    private async void Login(Identity identity)
    {
        try
        {
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.Portal) { OpenPortal(identity); await Poll(true, identity.Key); return; }
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey) { await ConnectApi(identity); return; }
            Directory.CreateDirectory(identity.Profile);
            var arguments = identity.Vendor == "claude" ? new[] { "auth", "login" }
                : identity.Vendor == "codex" ? new[] { "-c", "cli_auth_credentials_store='file'", "login" }
                : new[] { "auth", "add", "nous", "--type", "oauth" };
            await _monitor.Authenticate(identity, async cancellation => {
                using var process = Process.Start(Platform.Cli(identity.Vendor, identity.Profile, true, arguments))!;
                try { await process.WaitForExitAsync(cancellation); if (process.ExitCode != 0) throw new IOException("Anmeldung nicht abgeschlossen."); }
                finally { if (!process.HasExited && cancellation.IsCancellationRequested) process.Kill(true); }
            });
            if (!_closing) await Poll(true, identity.Key);
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception) { MessageBox.Show("Die Anbieter-CLI konnte nicht gestartet werden. Bitte Installation prüfen.", "UseScope"); }
    }
    private async Task Logout(Identity identity)
    {
        if (MessageBox.Show($"{identity.Title} bei UseScope abmelden?", "UseScope", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey)
            {
                await _monitor.Authenticate(identity, token => SecretVault.Delete(identity, token));
                _readings.Remove(identity.Key); Paint(); await Poll(true, identity.Key); return;
            }
            var arguments = identity.Vendor == "claude" ? new[] { "auth", "logout" } : identity.Vendor == "codex" ? new[] { "-c", "cli_auth_credentials_store='file'", "logout" } : new[] { "auth", "logout", "nous" };
            await _monitor.Authenticate(identity, async cancellation => {
                using var process = Process.Start(Platform.Cli(identity.Vendor, identity.Profile, false, arguments))!;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
                try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(output, error); }
                finally { if (!process.HasExited) process.Kill(true); }
                if (process.ExitCode != 0) throw new InvalidOperationException();
            });
            _readings[identity.Key] = new Reading(identity.Key, [], null, "Abgemeldet", null, true); Paint();
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception) { MessageBox.Show("Abmelden fehlgeschlagen. Bitte über die Anbieter-CLI prüfen.", "UseScope"); }
    }
    private void OpenPortal(Identity identity)
    {
        try { Providers.OpenPortal(identity.Vendor); }
        catch (Exception) { MessageBox.Show("Anbieterportal konnte nicht geöffnet werden. Bitte Standardbrowser prüfen.", "UseScope"); }
    }
    private async Task ConnectApi(Identity identity)
    {
        var provider = Providers.Get(identity.Vendor);
        using var dialog = new Form { Text = provider.Name, ClientSize = new Size(480, 252), StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var explanation = new Label { Left = 14, Top = 12, Width = 450, Height = 72, Text = provider.Help };
        var portal = new Button { Text = "Schlüssel im Anbieterportal erstellen…", Left = 14, Top = 88, Width = 300 };
        portal.Click += (_, _) => OpenPortal(identity);
        var key = new TextBox { Left = 14, Top = 146, Width = 450, UseSystemPasswordChar = true, MaxLength = 4096 };
        var team = new TextBox { Left = 14, Top = 202, Width = 450, PlaceholderText = "Team-ID (UUID) aus der xAI-Konsole", Visible = identity.Vendor == "xai" };
        var save = new Button { Text = "Speichern", Left = 254, Top = 210, Width = 100, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Abbrechen", Left = 364, Top = 210, Width = 100, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([explanation, portal, new Label { Left = 14, Top = 124, Text = "API-Schlüssel", Width = 450 }, key,
            new Label { Left = 14, Top = 178, Width = 450, Height = 28, Text = "Lokal für deinen Windows-Benutzer verschlüsselt gespeichert." }, save, cancel]);
        dialog.AcceptButton = save; dialog.CancelButton = cancel;
        if (identity.Vendor == "xai")
        {
            dialog.ClientSize = new Size(480, 308); save.Top += 56; cancel.Top += 56;
            foreach (var label in dialog.Controls.OfType<Label>().Where(label => label.Top == 178)) label.Top += 56;
            dialog.Controls.Add(new Label { Left = 14, Top = 180, Width = 450, Text = "Team-ID" }); dialog.Controls.Add(team);
        }
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var secret = ApiAccountSource.EncodeCredential(identity.Vendor, key.Text, team.Text); key.Clear();
            await _monitor.Authenticate(identity, token => SecretVault.Write(identity, secret, token));
            _readings.Remove(identity.Key); Paint(); await Poll(true, identity.Key);
        }
        catch (ArgumentException exception) { MessageBox.Show(exception.Message, "UseScope"); }
        catch (Exception) { MessageBox.Show("API-Schlüssel konnte nicht geschützt gespeichert werden.", "UseScope"); }
        finally { key.Clear(); }
    }
    private async void Remove(Identity identity)
    {
        var api = Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey;
        if (MessageBox.Show($"{identity.Title} aus der Anzeige entfernen?\n\n" + (api ? "Der lokal für UseScope gespeicherte API-Schlüssel wird gelöscht. Beim Anbieter wird er dadurch nicht widerrufen." : "Das Konto beim Anbieter und die lokale CLI-Anmeldung bleiben erhalten."), "UseScope", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (api) try { await _monitor.Authenticate(identity, token => SecretVault.Delete(identity, token)); }
            catch (Exception) { MessageBox.Show("Der lokale API-Schlüssel konnte nicht gelöscht werden. Der Eintrag bleibt bestehen.", "UseScope"); return; }
        Change(() => _preferences.Remove(identity)); _readings.Remove(identity.Key); Paint();
    }
    protected override void ExitThreadCore()
    {
        _closing = true; _clock.Stop(); _clock.Dispose(); _monitor.Dispose(); _shortcut.Dispose();
        _tray.Visible = false; _tray.Icon?.Dispose(); _tray.Dispose(); _menu.Dispose(); _view.Dispose();
        base.ExitThreadCore();
    }
}
