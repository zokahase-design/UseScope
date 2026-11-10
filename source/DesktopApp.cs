using System.Diagnostics;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal sealed class DesktopApp : ApplicationContext
{
    private readonly Preferences _preferences;
    private readonly Dashboard _view;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new() { ShowItemToolTips = false };
    private readonly AccountMonitor _monitor = new();
    private readonly Dictionary<string, Reading> _readings = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 15_000 };
    private readonly ShortcutWindow _shortcut;
    private readonly ShellOverlayGuard _shell;
    private bool _closing;
    private bool _paintPending;
    private bool _raisePending;
    internal DesktopApp(Preferences preferences)
    {
        _preferences = preferences;
        _view = new Dashboard(preferences) { ContextMenuStrip = _menu };
        _ = _view.Handle;
        _tray = CreateTrayIcon(_menu); _tray.Visible = true;
        _shell = new ShellOverlayGuard(_view, active => {
            _view.ShellOverlayActive(active);
            if (!active)
            {
                if (_paintPending) Paint();
                RaiseWidgetWhenReady();
            }
        });
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
        _view.OrderChanged += Paint;
        _view.AddRequested += Add;
        _view.PinRequested += () => Change(() => preferences.Pinned = !preferences.Pinned);
        _menu.Opening += (_, e) => {
            _view.MenuActive(true);
            try { FillMenu(); } catch { e.Cancel = true; _view.MenuActive(false); throw; }
        };
        _menu.Closed += (_, _) => {
            _view.MenuActive(false);
            if (_paintPending) Paint();
            RaiseWidgetWhenReady();
        };
        _monitor.Changed += reading => {
            if (!_closing) try { _view.BeginInvoke((Action)(() => {
                if (_monitor.IsCurrent(reading) && preferences.Identities.Any(item => item.Key == reading.Identity)) _readings[reading.Identity] = reading;
                Paint();
            })); } catch (InvalidOperationException) { }
        };
        _shortcut = new ShortcutWindow(preferences.Shortcut, Toggle);
        _clock.Tick += (_, _) => { Paint(); if (_view.Visible) _ = Poll(false); };
        _clock.Start();
        _shell.Poll(); Paint(); _view.Place(); _view.Show();
        _ = Poll(true);
        if (Platform.StartsWithWindows) Platform.StartsWithWindows = true;
    }
    private void Paint()
    {
        if (_closing) return;
        if (_menu.Visible || (_view.Visible && _shell.IsOpen)) { _paintPending = true; return; }
        _paintPending = false;
        _view.Render(_readings);
    }
    private async Task Poll(bool force, string? key = null)
    {
        if (_closing) return;
        var identities = _preferences.Identities;
        await MeterRuntime.Shared.StartIfNeeded(identities);
        try { await Task.Run(() => _monitor.Refresh(identities, force, key)); }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Account failures retain their visible status; no tray hover popups. */ }
    }
    private void Toggle()
    {
        if (_menu.Visible) _menu.Close();
        if (_view.Visible && (_preferences.Pinned || _view.IsInForeground)) HideWidget();
        else ShowWidget();
    }
    private void ShowWidget()
    {
        if (_closing) return;
        _shell.Poll();
        if (!_view.Visible)
        {
            Paint();
            _view.Place();
            _view.Show();
            _ = Poll(false);
        }
        _raisePending = true;
        RaiseWidgetWhenReady();
    }
    private void HideWidget()
    {
        _raisePending = false;
        _view.Hide();
    }
    private void RaiseWidgetWhenReady()
    {
        if (!_raisePending || _closing || _menu.Visible || _shell.IsOpen) return;
        _raisePending = false;
        if (_view.Visible) _view.RaiseForUser();
    }
    private void Change(Action mutation)
    {
        try { mutation(); _preferences.Save(); MeterRuntime.Shared.Update(_preferences.Identities); Paint(); }
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
        _menu.Items.Add(CreateMenuHeading());
        _menu.Items.Add(Command("Widget nach vorne holen", () => _view.BeginInvoke((Action)ShowWidget)));
        var hide = Command("Widget ausblenden", HideWidget);
        hide.Enabled = _view.Visible;
        _menu.Items.Add(hide);
        _menu.Items.Add(Command("Nutzung aktualisieren", () => _ = Poll(true)));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Command("Konto hinzufügen…", Add));
        var accounts = new ToolStripMenuItem("Konten");
        foreach (var identity in _preferences.Identities)
        {
            var observed = _readings.GetValueOrDefault(identity.Key);
            var title = observed?.Status.Contains('@') == true ? observed.Status : identity.Title;
            var account = new ToolStripMenuItem(title) { Image = Artwork.ProviderImage(identity.Vendor, _preferences.Theme) };
            account.DropDownItems.Add(new ToolStripMenuItem(observed?.Failed == true ? observed.Status : "Kontoverbindung") { Enabled = false });
            var provider = Providers.Get(identity.Vendor);
            var meter = MeterStorage.Enabled(identity);
            account.DropDownItems.Add(Command("Werte und Verbindung erklären…", () => MessageBox.Show(ReadingPresentation.Detail(identity, _readings.GetValueOrDefault(identity.Key)) + "\n\n" + provider.Help, provider.Name)));
            account.DropDownItems.Add(Command("Nutzung aktualisieren", () => _ = Poll(true, identity.Key)));
            account.DropDownItems.Add(new ToolStripSeparator());
            account.DropDownItems.Add(Command("Guthaben / Credits anzeigen", () => Change(() => _preferences.SetBalance(identity, !identity.ShowBalance)), identity.ShowBalance));
            if (LocalTokenReader.DirectoryFor(identity) is not null)
                account.DropDownItems.Add(Command("Lokalen Claude-Verlauf trennen", () => _ = DisconnectLocalHistory(identity)));
            account.DropDownItems.Add(new ToolStripSeparator());
            if (meter || provider.Connection != ConnectionKind.Portal)
                account.DropDownItems.Add(Command(meter ? "API-Zähler einrichten…" : provider.Connection == ConnectionKind.Native ? "Anmelden…" : "API-Schlüssel hinterlegen…", () => Login(identity)));
            if (meter) account.DropDownItems.Add(Command("API-Adresse und lokalen Schlüssel…", () => _ = ShowMeterClient(identity)));
            if (meter) account.DropDownItems.Add(Command("API-Verbindung ohne Generierung prüfen", () => _ = CheckMeter(identity)));
            account.DropDownItems.Add(Command("Offizielles Anbieterportal…", () => OpenPortal(identity)));
            if (meter || provider.Connection != ConnectionKind.Portal) account.DropDownItems.Add(Command(meter || provider.Connection == ConnectionKind.ApiKey ? "API-Schlüssel lokal löschen…" : "Abmelden…", () => _ = Logout(identity)));
            account.DropDownItems.Add(new ToolStripSeparator());
            account.DropDownItems.Add(Command("Nach oben", () => Change(() => _preferences.Move(identity, -1))));
            account.DropDownItems.Add(Command("Nach unten", () => Change(() => _preferences.Move(identity, 1))));
            account.DropDownItems.Add(Command("Aus Anzeige entfernen…", () => Remove(identity)));
            accounts.DropDownItems.Add(account);
        }
        if (accounts.DropDownItems.Count == 0) accounts.DropDownItems.Add(new ToolStripMenuItem("Noch keine Konten") { Enabled = false });
        _menu.Items.Add(accounts);
        _menu.Items.Add(new ToolStripSeparator());
        var display = new ToolStripMenuItem("Darstellung");
        var themes = new ToolStripMenuItem("Farbschema");
        themes.DropDownItems.Add(Command("Hell", () => Change(() => _preferences.Theme = "Hell"), _preferences.Theme == "Hell"));
        themes.DropDownItems.Add(Command("Dunkel", () => Change(() => _preferences.Theme = "Dark"), _preferences.Theme == "Dark"));
        display.DropDownItems.Add(themes);
        var quota = new ToolStripMenuItem("Limitbalken");
        quota.DropDownItems.Add(Command("Verbraucht · Balken füllen sich", () => Change(() => _preferences.ShowRemainingQuota = false), !_preferences.ShowRemainingQuota));
        quota.DropDownItems.Add(Command("Verfügbar · Balken leeren sich", () => Change(() => _preferences.ShowRemainingQuota = true), _preferences.ShowRemainingQuota));
        display.DropDownItems.Add(quota);
        var location = new ToolStripMenuItem("Position");
        foreach (var (key, text) in new[] { ("BottomRight", "Unten rechts"), ("TopRight", "Oben rechts"), ("TopLeft", "Oben links"), ("BottomLeft", "Unten links"), ("Center", "Bildschirmmitte"), ("NearCursor", "Am Mauszeiger"), ("LastPosition", "Letzte Position") })
            location.DropDownItems.Add(Command(text, () => { Change(() => _preferences.Position = key); _view.Place(); }, _preferences.Position == key));
        display.DropDownItems.Add(location);
        display.DropDownItems.Add(new ToolStripSeparator());
        display.DropDownItems.Add(Command("Immer im Vordergrund", () => Change(() => _preferences.Pinned = !_preferences.Pinned), _preferences.Pinned));
        _menu.Items.Add(display);
        _menu.Items.Add(Command("Mit Windows starten", () => Change(() => Platform.StartsWithWindows = !Platform.StartsWithWindows), Platform.StartsWithWindows));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Command("UseScope beenden", ExitThread));
        StyleMenu(_menu, Colors.For(_preferences.Theme));
    }
    internal static void StyleMenu(ToolStrip strip, Colors palette)
    {
        strip.ShowItemToolTips = false;
        strip.BackColor = palette.Surface; strip.ForeColor = palette.Ink;
        strip.Renderer = new MenuRenderer(palette);
        foreach (ToolStripItem item in strip.Items)
        {
            item.ForeColor = palette.Ink;
            if (item is ToolStripMenuItem child && child.HasDropDownItems) StyleMenu(child.DropDown, palette);
        }
    }
    internal static ToolStripLabel CreateMenuHeading() => new(AppInfo.MenuTitle);
    internal static NotifyIcon CreateTrayIcon(ContextMenuStrip menu) => new() { Text = "", Icon = Artwork.CreateIcon(), ContextMenuStrip = menu };
    private static void DisposeImages(ToolStripItem item)
    {
        if (item is ToolStripMenuItem child)
            foreach (ToolStripItem descendant in child.DropDownItems) DisposeImages(descendant);
        item.Image?.Dispose(); item.Image = null;
    }
    private sealed class MenuRenderer(Colors colors) : ToolStripProfessionalRenderer(new MenuPalette(colors))
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnRenderMenuItemBackground(e); return; }
            var active = e.Item.Enabled && (e.Item.Selected || e.Item.Pressed);
            using var fill = new SolidBrush(active ? colors.Track : colors.Surface);
            e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (SystemInformation.HighContrast) { base.OnRenderItemText(e); return; }
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle,
                e.Item.Enabled ? colors.Ink : colors.Muted, e.TextFormat);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (!SystemInformation.HighContrast) e.ArrowColor = e.Item?.Enabled == false ? colors.Muted : colors.Ink;
            base.OnRenderArrow(e);
        }
    }
    private sealed class MenuPalette : ProfessionalColorTable
    {
        private readonly Colors colors;
        internal MenuPalette(Colors palette) { colors = palette; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => colors.Surface;
        public override Color MenuBorder => colors.Line;
        public override Color ImageMarginGradientBegin => colors.Surface;
        public override Color ImageMarginGradientMiddle => colors.Surface;
        public override Color ImageMarginGradientEnd => colors.Surface;
        public override Color MenuItemSelected => colors.Track;
        public override Color MenuItemSelectedGradientBegin => colors.Track;
        public override Color MenuItemSelectedGradientEnd => colors.Track;
        public override Color MenuItemPressedGradientBegin => colors.Track;
        public override Color MenuItemPressedGradientMiddle => colors.Track;
        public override Color MenuItemPressedGradientEnd => colors.Track;
        public override Color MenuItemBorder => colors.Line;
        public override Color CheckBackground => colors.Track;
        public override Color CheckSelectedBackground => colors.Track;
        public override Color CheckPressedBackground => colors.Track;
        public override Color SeparatorDark => colors.Line;
        public override Color SeparatorLight => colors.Surface;
    }
    private void Add()
    {
        using var dialog = new Form { Text = "Konto hinzufügen", ClientSize = new Size(440, 310), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var provider = new ComboBox { Left = 14, Top = 34, Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
        provider.Width = 410; provider.Items.AddRange(Providers.All.Select(item => item.Name).ToArray()); provider.SelectedIndex = 0;
        var hint = new Label { Left = 14, Top = 66, Width = 410, Height = 82, Text = Providers.All[0].Help };
        var meter = new CheckBox { Text = "Lokaler API-Zähler (nur durchgeleitete Nutzung)", Left = 14, Top = 152, Width = 410 };
        void SelectProvider() { var selected = Providers.All[provider.SelectedIndex]; hint.Text = selected.Help; meter.Checked = selected.Connection == ConnectionKind.LocalMeter; meter.Enabled = selected.Connection != ConnectionKind.LocalMeter && ProviderCatalog.AllowsMeter(selected); }
        provider.SelectedIndexChanged += (_, _) => SelectProvider(); SelectProvider();
        var name = new TextBox { Left = 14, Top = 212, Width = 410 };
        var add = new Button { Text = "Hinzufügen", Left = 218, Top = 266, Width = 110, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Abbrechen", Left = 334, Top = 266, Width = 90, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange([new Label { Text = "Anbieter", Left = 14, Top = 12 }, provider, hint, meter, new Label { Text = "Bezeichnung (optional, z. B. E-Mail oder Projekt)", Left = 14, Top = 188, Width = 410 }, name, add, cancel]);
        dialog.AcceptButton = add; dialog.CancelButton = cancel;
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var vendor = Providers.All[provider.SelectedIndex].Id;
            var title = string.IsNullOrWhiteSpace(name.Text) ? Artwork.VendorName(vendor) + " " + (_preferences.Identities.Count + 1) : name.Text.Trim();
            var identity = _preferences.Add(vendor, title);
            if (meter.Checked) { _preferences.SetConnection(identity, MeterSetup.Options()); identity = _preferences.Identities.Single(item => item.Key == identity.Key); }
            Paint(); Login(identity);
        }
        catch (Exception) { MessageBox.Show("Konto konnte nicht hinzugefügt werden.", "UseScope"); }
    }
    private async Task DisconnectLocalHistory(Identity identity)
    {
        try
        {
            await _monitor.Authenticate(identity, _ =>
            {
                var current = _preferences.Identities.Single(item => item.Key == identity.Key);
                _preferences.SetConnection(current, LocalTokenReader.WithoutDirectory(current));
                return Task.CompletedTask;
            });
            _readings.Remove(identity.Key); Paint(); await Poll(true, identity.Key);
        }
        catch (Exception) { MessageBox.Show(_view, "Lokaler Claude-Verlauf konnte nicht getrennt werden.", "UseScope"); }
    }
    private async void Login(Identity identity)
    {
        try
        {
            if (MeterStorage.Enabled(identity)) { await ConnectMeter(identity); return; }
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.Portal) { OpenPortal(identity); await Poll(true, identity.Key); return; }
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey) { await ConnectApi(identity); return; }
            if (identity.Vendor == "gemini")
            {
                using var options = new GoogleOptionsDialog(identity);
                if (options.ShowDialog() != DialogResult.OK) return;
                _preferences.SetConnection(identity, options.Options());
                identity = _preferences.Identities.Single(item => item.Key == identity.Key);
            }
            Directory.CreateDirectory(identity.Profile);
            if (identity.Vendor is "alibaba-coding" or "alibaba-token")
            {
                using var site = new AlibabaSiteDialog(identity);
                if (site.ShowDialog() != DialogResult.OK) return;
                _preferences.SetConnection(identity, site.Options()); identity = _preferences.Identities.Single(item => item.Key == identity.Key);
            }
            var arguments = NativeConnections.Login(identity);
            _readings.Remove(identity.Key); Paint();
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
            if (MeterStorage.Enabled(identity))
            {
                await _monitor.Authenticate(identity, token => MeterStorage.DeleteCredential(identity, token));
                _readings.Remove(identity.Key); Paint(); await Poll(true, identity.Key); return;
            }
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey)
            {
                await _monitor.Authenticate(identity, token => SecretVault.Delete(identity, token));
                _readings.Remove(identity.Key); Paint(); await Poll(true, identity.Key); return;
            }
            var arguments = NativeConnections.Logout(identity.Vendor);
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
        using var dialog = new ApiCredentialDialog(Providers.Get(identity.Vendor));
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var secret = dialog.Encode(identity.Vendor);
            await _monitor.Authenticate(identity, token => SecretVault.Write(identity, secret, token));
            _readings.Remove(identity.Key); Paint(); await Poll(true, identity.Key);
        }
        catch (ArgumentException exception) { MessageBox.Show(exception.Message, "UseScope"); }
        catch (Exception) { MessageBox.Show("Zugang konnte nicht geschützt gespeichert werden.", "UseScope"); }
    }
    private async Task ConnectMeter(Identity identity)
    {
        try
        {
            using var dialog = new ApiCredentialDialog(Providers.Get(identity.Vendor), true);
            if (dialog.ShowDialog() != DialogResult.OK) return;
            var old = await MeterStorage.ReadCredential(identity, default);
            var credential = dialog.Meter(old?.LocalToken);
            MeterRuntime.Shared.Update(_preferences.Identities); await MeterRuntime.Shared.EnsureStarted();
            await _monitor.Authenticate(identity, token => MeterStorage.WriteCredential(identity, credential, token));
            _readings.Remove(identity.Key); Paint(); await Poll(true, identity.Key); await ShowMeterClient(identity);
        }
        catch (ArgumentException error) { MessageBox.Show(error.Message, "UseScope"); }
        catch (Exception) { MessageBox.Show("API-Zähler konnte nicht eingerichtet werden. Geschützten Speicher und lokalen Port 48371 prüfen.", "UseScope"); }
    }
    private async Task ShowMeterClient(Identity identity)
    {
        try
        {
            var credential = await MeterStorage.ReadCredential(identity, default);
            if (credential is null) { MessageBox.Show("Bitte zuerst den API-Zähler einrichten.", "UseScope"); return; }
            using var dialog = new Form { Text = "UseScope · API-Client einrichten", ClientSize = new Size(550, 290), StartPosition = FormStartPosition.CenterScreen };
            var url = LoopbackMeterServer.ClientUrl(identity);
            var address = new TextBox { Left = 14, Top = 76, Width = 520, ReadOnly = true, Text = url };
            var key = new TextBox { Left = 14, Top = 116, Width = 520, ReadOnly = true, Text = credential.LocalToken, UseSystemPasswordChar = true };
            var copyUrl = new Button { Text = "Adresse kopieren", Left = 14, Top = 158, Width = 180 };
            var copyKey = new Button { Text = "Lokalen Schlüssel kopieren", Left = 202, Top = 158, Width = 220 };
            copyUrl.Click += (_, _) => { try { Clipboard.SetText(url); } catch { MessageBox.Show("Zwischenablage derzeit nicht verfügbar."); } };
            copyKey.Click += (_, _) => { try { Clipboard.SetText(credential.LocalToken); } catch { MessageBox.Show("Zwischenablage derzeit nicht verfügbar."); } };
            dialog.Controls.AddRange([new Label { Text = "Im KI-Client die API-Basisadresse und diesen lokalen Schlüssel eintragen. Bei Gemini die Adresse bis einschließlich /v1 durch /v1beta ersetzen; das Client-Protokoll bleibt gleich.", Left = 14, Top = 14, Width = 520, Height = 54 }, address, key, copyUrl, copyKey,
                new Label { Text = "UseScope muss laufen. Für Streaming nach Möglichkeit usage / include_usage im Client aktivieren. Nur hierüber geleitete Nutzung wird gezählt. Der lokale Schlüssel erlaubt kostenpflichtige Anfragen bei deinem Anbieter; vertraulich behandeln.", Left = 14, Top = 206, Width = 520, Height = 66 }]);
            dialog.ShowDialog(); key.Clear();
        }
        catch (Exception) { MessageBox.Show("Zähler-Konfiguration konnte nicht gelesen werden.", "UseScope"); }
    }
    private async Task CheckMeter(Identity identity)
    {
        try { MessageBox.Show(await MeterRuntime.Shared.CheckConnection(identity), "UseScope · Verbindungstest"); }
        catch (Exception) { MessageBox.Show("Verbindungstest nicht möglich. UseScope-Port und Internetverbindung prüfen.", "UseScope"); }
    }
    private async void Remove(Identity identity)
    {
        var meter = MeterStorage.Enabled(identity); var api = meter || Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey;
        if (MessageBox.Show($"{identity.Title} aus der Anzeige entfernen?\n\n" + (api ? "Der lokal für UseScope gespeicherte API-Schlüssel wird gelöscht. Beim Anbieter wird er dadurch nicht widerrufen." : "Das Konto beim Anbieter und die lokale CLI-Anmeldung bleiben erhalten."), "UseScope", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (api) try { await _monitor.Authenticate(identity, token => meter ? MeterStorage.DeleteCredential(identity, token) : SecretVault.Delete(identity, token)); }
            catch (Exception) { MessageBox.Show("Der lokale API-Schlüssel konnte nicht gelöscht werden. Der Eintrag bleibt bestehen.", "UseScope"); return; }
        Change(() => _preferences.Remove(identity)); _readings.Remove(identity.Key); Paint();
    }
    protected override void ExitThreadCore()
    {
        _closing = true; _clock.Stop(); _clock.Dispose(); _shell.Dispose(); _monitor.Dispose(); _shortcut.Dispose();
        Task.Run(async () => await MeterRuntime.Shared.DisposeAsync()).GetAwaiter().GetResult();
        _tray.Visible = false; _tray.Icon?.Dispose(); _tray.Dispose(); _menu.Dispose(); _view.Dispose();
        base.ExitThreadCore();
    }
}
