using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Diagnostics;

namespace UseScope.Desktop;

internal sealed class MacApp : Application
{
    private Preferences _preferences = null!;
    private Widget _widget = null!;
    private AccountMonitor _monitor = null!;
    private readonly Dictionary<string, Reading> _readings = [];
    private readonly NativeMenu _menu = new();
    private TrayIcon _tray = null!;
    private DispatcherTimer _timer = null!;
    private bool _menuOpen, _exiting;
    private Action? _refreshAccounts;
    private Window? _accountsWindow;
    internal static bool OfflineRender;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (OfflineRender) { base.OnFrameworkInitializationCompleted(); return; }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _preferences = new Preferences(Path.Combine(Preferences.DataDirectory, "settings.json"));
            _monitor = new AccountMonitor();
            _widget = new Widget(_preferences, _readings);
            _widget.Pin += () => Change(() => _preferences.Pinned = !_preferences.Pinned);
            _widget.Order += () => _ = Manage();
            _widget.Add += () => _ = Manage(true);
            _widget.Closing += (_, args) => { if (!_exiting) { args.Cancel = true; _widget.Hide(); } };
            desktop.MainWindow = _widget;
            _tray = new TrayIcon { Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "UseScope.ico")), Menu = _menu, IsVisible = true };
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
            _menu.Opening += (_, _) => { _menuOpen = true; _widget.Topmost = false; FillMenu(); };
            _menu.Closed += (_, _) => { _menuOpen = false; RefreshView(); };
            _monitor.Changed += reading => Dispatcher.UIThread.Post(() => {
                if (!_exiting && _monitor.IsCurrent(reading) && _preferences.Identities.Any(identity => identity.Key == reading.Identity))
                { _readings[reading.Identity] = reading; RefreshView(); _refreshAccounts?.Invoke(); }
            });
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += (_, _) => { RefreshView(); if (_widget.IsVisible) _ = Poll(false); }; _timer.Start();
            desktop.Exit += (_, _) => { _exiting = true; _timer.Stop(); _monitor.Dispose(); Task.Run(async () => await MeterRuntime.Shared.DisposeAsync()).GetAwaiter().GetResult(); _tray.Dispose(); _widget.DisposeImages(); };
            FillMenu(); RefreshView(); _widget.Place(); _ = Poll(true);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private void RefreshView()
    {
        if (_exiting) return;
        if (_menuOpen) return;
        RequestedThemeVariant = _preferences.Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        _widget.Update();
    }
    private async Task Poll(bool force, string? key = null)
    {
        var identities = _preferences.Identities;
        await MeterRuntime.Shared.StartIfNeeded(identities);
        try { await Task.Run(() => _monitor.Refresh(identities, force, key)); }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_exiting) await Notice("Die Aktualisierung ist derzeit nicht möglich."); }
    }
    private void Change(Action action)
    {
        try { action(); _preferences.Save(); MeterRuntime.Shared.Update(_preferences.Identities); RefreshView(); }
        catch (Exception) { _ = Notice("Die Änderung konnte nicht gespeichert werden."); }
    }
    private static NativeMenuItem Item(string text, Action action, bool? selected = null)
    {
        var item = new NativeMenuItem(text);
        if (selected.HasValue) { item.ToggleType = NativeMenuItemToggleType.CheckBox; item.IsChecked = selected.Value; }
        item.Click += (_, _) => action(); return item;
    }
    private void FillMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new NativeMenuItem(AppInfo.MenuTitle) { IsEnabled = false });
        _menu.Items.Add(Item("Widget anzeigen / ausblenden", () => { if (_widget.IsVisible) _widget.Hide(); else { _widget.Show(); _widget.Place(); _ = Poll(false); } }));
        _menu.Items.Add(Item("Nutzung aktualisieren", () => _ = Poll(true)));
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Item("Konto hinzufügen…", () => _ = Manage(true)));
        _menu.Items.Add(Item("Konten verwalten…", () => _ = Manage()));
        _menu.Items.Add(new NativeMenuItemSeparator());
        var appearance = new NativeMenu();
        var quota = new NativeMenu();
        quota.Items.Add(Item("Verbraucht · Balken füllen sich", () => Change(() => _preferences.ShowRemainingQuota = false), !_preferences.ShowRemainingQuota));
        quota.Items.Add(Item("Verfügbar · Balken leeren sich", () => Change(() => _preferences.ShowRemainingQuota = true), _preferences.ShowRemainingQuota));
        appearance.Items.Add(new NativeMenuItem("Limitbalken") { Menu = quota });
        appearance.Items.Add(Item("Hell", () => Change(() => _preferences.Theme = "Hell"), _preferences.Theme == "Hell"));
        appearance.Items.Add(Item("Dunkel", () => Change(() => _preferences.Theme = "Dark"), _preferences.Theme == "Dark"));
        appearance.Items.Add(Item("Immer im Vordergrund", () => Change(() => _preferences.Pinned = !_preferences.Pinned), _preferences.Pinned));
        _menu.Items.Add(new NativeMenuItem("Darstellung") { Menu = appearance });
        _menu.Items.Add(new NativeMenuItemSeparator());
        _menu.Items.Add(Item("UseScope beenden", () => { _exiting = true; (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(); }));
    }
    private async Task Notice(string message)
    {
        if (_exiting) return;
        var box = new Window { Title = "UseScope", Width = 480, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => box.Close();
        box.Content = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, ok } };
        if (!_widget.IsVisible) _widget.Show();
        await box.ShowDialog(_widget);
    }
    private async Task Manage(bool adding = false)
    {
        if (_accountsWindow is not null) { _accountsWindow.Activate(); return; }
        var window = new Window { Title = adding ? "UseScope · Konto hinzufügen" : "UseScope · Konten", Width = 640, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        _accountsWindow = window;
        var list = new StackPanel { Spacing = 8 };
        var vendor = new ComboBox { ItemsSource = Providers.All.Select(item => item.Name).ToArray(), SelectedIndex = 0, Width = 280 };
        var title = new TextBox { Watermark = "Bezeichnung", Width = 150 };
        var add = new Button { Content = "Konto hinzufügen" };
        var hint = new TextBlock { Text = Providers.All[0].Help, TextWrapping = TextWrapping.Wrap, FontSize = 11 };
        var meter = new CheckBox { Content = "Lokaler API-Zähler (nur durchgeleitete Nutzung)" };
        void SelectProvider() { if (vendor.SelectedIndex < 0) return; var selected = Providers.All[vendor.SelectedIndex]; hint.Text = selected.Help; meter.IsChecked = selected.Connection == ConnectionKind.LocalMeter; meter.IsEnabled = selected.Connection != ConnectionKind.LocalMeter && ProviderCatalog.AllowsMeter(selected); }
        vendor.SelectionChanged += (_, _) => SelectProvider(); SelectProvider();
        var footer = new StackPanel { Spacing = 8, Children = { hint, meter, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { vendor, title, add } } } };
        var root = new DockPanel { Margin = new Thickness(14) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = list }); window.Content = root;
        void Rebuild()
        {
            list.Children.Clear();
            foreach (var identity in _preferences.Identities)
            {
                var provider = Providers.Get(identity.Vendor);
                var heading = new TextBlock { Text = identity.Title + " · " + provider.Name, FontWeight = FontWeight.Bold };
                var reading = _readings.GetValueOrDefault(identity.Key);
                var status = new TextBlock { Text = reading?.Status ?? "Noch nicht abgefragt", FontSize = 11 };
                var actions = new WrapPanel { Orientation = Orientation.Horizontal };
                Button Action(string text, Action callback) { var button = new Button { Content = text, FontSize = 11 }; button.Click += (_, _) => callback(); actions.Children.Add(button); return button; }
                var local = MeterStorage.Enabled(identity);
                Action(local ? "API-Zähler einrichten" : provider.Connection == ConnectionKind.ApiKey ? "API-Schlüssel" : provider.Connection == ConnectionKind.Portal ? "Portal" : "Anmelden", () => _ = Login(identity));
                if (local) Action("Client-Zugang", () => _ = ShowMeterClient(identity));
                if (local) Action("Verbindung prüfen", () => _ = CheckMeter(identity));
                Action("Infos", () => _ = Notice(ReadingPresentation.Detail(_preferences.Identities.FirstOrDefault(item => item.Key == identity.Key) ?? identity, _readings.GetValueOrDefault(identity.Key)) + "\n\n" + provider.Help));
                if (LocalTokenReader.DirectoryFor(identity) is not null)
                    Action("Lokalen Claude-Verlauf trennen", () => _ = DisconnectLocalHistory(identity));
                Action("Anbieterportal", () => OpenPortal(identity));
                if (local || provider.Connection != ConnectionKind.Portal) Action(local || provider.Connection == ConnectionKind.ApiKey ? "Schlüssel löschen" : "Abmelden", () => _ = Logout(identity));
                Action("↑", () => { Change(() => _preferences.Move(identity, -1)); Rebuild(); });
                Action("↓", () => { Change(() => _preferences.Move(identity, 1)); Rebuild(); });
                Action("Entfernen", () => _ = Remove(identity, Rebuild));
                var balance = new CheckBox { Content = "Guthaben", IsChecked = identity.ShowBalance, FontSize = 11 };
                balance.IsCheckedChanged += (_, _) => { Change(() => _preferences.SetBalance(identity, balance.IsChecked == true)); Rebuild(); }; actions.Children.Add(balance);
                list.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 8), Children = { heading, status, actions } });
            }
        }
        add.Click += (_, _) => {
            try { var provider = Providers.All[vendor.SelectedIndex].Id;
                var identity = _preferences.Add(provider, string.IsNullOrWhiteSpace(title.Text) ? provider : title.Text.Trim());
                if (meter.IsChecked == true) { _preferences.SetConnection(identity, MeterSetup.Options()); identity = _preferences.Identities.Single(item => item.Key == identity.Key); }
                Rebuild(); RefreshView(); _ = Login(identity); }
            catch (Exception) { _ = Notice("Konto konnte nicht gespeichert werden."); }
        };
        Rebuild(); _refreshAccounts = Rebuild;
        if (adding) window.Opened += (_, _) => vendor.Focus();
        try { if (!_widget.IsVisible) _widget.Show(); await window.ShowDialog(_widget); }
        finally { _refreshAccounts = null; _accountsWindow = null; }
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
            _readings.Remove(identity.Key); RefreshView(); _refreshAccounts?.Invoke(); await Poll(true, identity.Key);
        }
        catch (Exception) { await Notice("Lokaler Claude-Verlauf konnte nicht getrennt werden."); }
    }
    private async Task<bool> Confirm(string message)
    {
        var result = false; var dialog = new Window { Title = "UseScope", Width = 430, Height = 190, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var yes = new Button { Content = "Bestätigen" }; var no = new Button { Content = "Abbrechen" };
        yes.Click += (_, _) => { result = true; dialog.Close(); }; no.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 15, Children = {
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { no, yes } } } };
        if (!_widget.IsVisible) _widget.Show(); await dialog.ShowDialog(_widget); return result;
    }
    private async Task Remove(Identity identity, Action refresh)
    {
        var meter = MeterStorage.Enabled(identity); var api = meter || Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey;
        if (!await Confirm(identity.Title + " aus der Anzeige entfernen? " + (api ? "Der lokale UseScope-API-Schlüssel wird gelöscht; beim Anbieter wird er dadurch nicht widerrufen." : "Anbieter-Konto und lokale Anmeldung bleiben erhalten."))) return;
        if (api) try { await _monitor.Authenticate(identity, token => meter ? MeterStorage.DeleteCredential(identity, token) : SecretVault.Delete(identity, token)); }
            catch (Exception) { await Notice("Der lokale API-Schlüssel konnte nicht gelöscht werden. Der Eintrag bleibt bestehen."); return; }
        Change(() => _preferences.Remove(identity)); _readings.Remove(identity.Key); refresh(); RefreshView();
    }
    private async Task Login(Identity identity)
    {
        if (MeterStorage.Enabled(identity)) { await ConnectApi(identity); return; }
        if (Providers.Get(identity.Vendor).Connection == ConnectionKind.Portal) { OpenPortal(identity); await Poll(true, identity.Key); return; }
        if (Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey) { await ConnectApi(identity); return; }
        try {
            if (identity.Vendor == "gemini") { if (!await ConfigureGoogle(identity)) return; identity = _preferences.Identities.Single(item => item.Key == identity.Key); }
            if (identity.Vendor is "alibaba-coding" or "alibaba-token") { if (!await ConfigureAlibaba(identity)) return; identity = _preferences.Identities.Single(item => item.Key == identity.Key); }
            _readings.Remove(identity.Key); RefreshView(); _refreshAccounts?.Invoke();
            await _monitor.Authenticate(identity, token => Platform.Login(identity, token)); await Poll(true, identity.Key);
        }
        catch (OperationCanceledException) { if (!_exiting) await Notice("Anmeldung abgebrochen oder Zeitlimit erreicht."); }
        catch (Exception) { await Notice("Anmeldung konnte nicht abgeschlossen werden. CLI-Installation und Terminal prüfen."); }
    }
    private async Task Logout(Identity identity)
    {
        if (!await Confirm(identity.Title + " abmelden?")) return;
        try
        {
            if (MeterStorage.Enabled(identity))
            {
                await _monitor.Authenticate(identity, token => MeterStorage.DeleteCredential(identity, token));
                _readings.Remove(identity.Key); RefreshView(); _refreshAccounts?.Invoke(); await Poll(true, identity.Key); return;
            }
            if (Providers.Get(identity.Vendor).Connection == ConnectionKind.ApiKey)
            {
                await _monitor.Authenticate(identity, token => SecretVault.Delete(identity, token));
                _readings.Remove(identity.Key); RefreshView(); _refreshAccounts?.Invoke(); await Poll(true, identity.Key); return;
            }
            await _monitor.Authenticate(identity, async lifetime => {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var args = NativeConnections.Logout(identity.Vendor);
                using var process = Process.Start(Platform.Cli(identity.Vendor, identity.Profile, false, args))!;
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
                try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(output, error); }
                finally { if (!process.HasExited) process.Kill(true); }
                if (process.ExitCode != 0) throw new IOException();
            });
            _readings[identity.Key] = new(identity.Key, [], null, "Abgemeldet", null, true); RefreshView(); _refreshAccounts?.Invoke();
        }
        catch (Exception) { await Notice("Abmelden fehlgeschlagen. Bitte die Anbieter-CLI prüfen."); }
    }
    private void OpenPortal(Identity identity)
    {
        try { Providers.OpenPortal(identity.Vendor); }
        catch (Exception) { _ = Notice("Anbieterportal konnte nicht geöffnet werden. Bitte Standardbrowser prüfen."); }
    }
    private async Task ConnectApi(Identity identity)
    {
        var provider = Providers.Get(identity.Vendor);
        var meter = MeterStorage.Enabled(identity);
        var fields = meter ? MeterSetup.Fields(identity.Vendor) : ConnectionSetup.Fields(identity.Vendor);
        var dialog = new Window { Title = provider.Name, Width = 560, Height = 240 + fields.Count * 64, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        content.Children.Add(new TextBlock { Text = meter ? MeterSetup.Help : provider.Help, TextWrapping = TextWrapping.Wrap });
        var portal = new Button { Content = "Offizielles Anbieterportal öffnen…" }; portal.Click += (_, _) => OpenPortal(identity); content.Children.Add(portal);
        var boxes = new Dictionary<string, TextBox>();
        foreach (var field in fields)
        {
            content.Children.Add(new TextBlock { Text = field.Label });
            var box = new TextBox { Text = field.Default, MaxLength = 4096 };
            if (field.Secret) box.PasswordChar = '●'; boxes[field.Id] = box; content.Children.Add(box);
        }
        content.Children.Add(new TextBlock { Text = "Zugangsdaten lokal im macOS-Schlüsselbund gespeichert.", FontSize = 11 });
        var save = new Button { Content = "Speichern" }; var cancel = new Button { Content = "Abbrechen" }; var confirmed = false;
        save.Click += (_, _) => { confirmed = true; dialog.Close(); }; cancel.Click += (_, _) => dialog.Close();
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { cancel, save } }); dialog.Content = new ScrollViewer { Content = content };
        if (!_widget.IsVisible) _widget.Show(); await dialog.ShowDialog(_widget);
        try
        {
            if (!confirmed) return;
            var values = boxes.ToDictionary(item => item.Key, item => item.Value.Text ?? "");
            if (meter)
            {
                var old = await MeterStorage.ReadCredential(identity, default); var credential = MeterSetup.Encode(values, old?.LocalToken);
                MeterRuntime.Shared.Update(_preferences.Identities); await MeterRuntime.Shared.EnsureStarted();
                await _monitor.Authenticate(identity, token => MeterStorage.WriteCredential(identity, credential, token));
                _readings.Remove(identity.Key); RefreshView(); _refreshAccounts?.Invoke(); await Poll(true, identity.Key); await ShowMeterClient(identity); return;
            }
            var secret = ConnectionSetup.Encode(identity.Vendor, values);
            foreach (var box in boxes.Values) box.Text = "";
            await _monitor.Authenticate(identity, token => SecretVault.Write(identity, secret, token));
            _readings.Remove(identity.Key); RefreshView(); _refreshAccounts?.Invoke(); await Poll(true, identity.Key);
        }
        catch (ArgumentException exception) { await Notice(exception.Message); }
        catch (Exception) { await Notice("Zugang konnte nicht im Schlüsselbund gespeichert werden."); }
        finally { foreach (var box in boxes.Values) box.Text = ""; }
    }
    private async Task ShowMeterClient(Identity identity)
    {
        try
        {
            var credential = await MeterStorage.ReadCredential(identity, default);
            if (credential is null) { await Notice("Bitte zuerst den API-Zähler einrichten."); return; }
            var dialog = new Window { Title = "UseScope · API-Client einrichten", Width = 590, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var url = LoopbackMeterServer.ClientUrl(identity); var copyUrl = new Button { Content = "Adresse kopieren" }; var copyKey = new Button { Content = "Lokalen Schlüssel kopieren" };
            copyUrl.Click += async (_, _) => { try { if (dialog.Clipboard is { } clipboard) await clipboard.SetTextAsync(url); } catch { await Notice("Zwischenablage nicht verfügbar."); } };
            copyKey.Click += async (_, _) => { try { if (dialog.Clipboard is { } clipboard) await clipboard.SetTextAsync(credential.LocalToken); } catch { await Notice("Zwischenablage nicht verfügbar."); } };
            var key = new TextBox { Text = credential.LocalToken, IsReadOnly = true, PasswordChar = '●' }; var close = new Button { Content = "Schließen" }; close.Click += (_, _) => dialog.Close();
            dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 14, Children = {
                new TextBlock { Text = "Im KI-Client API-Basisadresse und lokalen Schlüssel eintragen. Das Protokoll bleibt das deines Anbieters; Gemini verwendet /v1beta statt /v1. UseScope muss laufen.", TextWrapping = TextWrapping.Wrap },
                new TextBox { Text = url, IsReadOnly = true }, key, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copyUrl, copyKey } },
                new TextBlock { Text = "Streaming: include_usage aktivieren, wenn der Anbieter dies unterstützt. Der lokale Schlüssel erlaubt kostenpflichtige Anfragen und ist vertraulich. Nur durchgeleitete Nutzung wird gezählt.", TextWrapping = TextWrapping.Wrap }, close } };
            if (!_widget.IsVisible) _widget.Show(); await dialog.ShowDialog(_widget); key.Text = "";
        }
        catch (Exception) { await Notice("API-Zähler konnte nicht gelesen werden. Geschützten Speicher prüfen."); }
    }
    private async Task CheckMeter(Identity identity)
    {
        try { await Notice(await MeterRuntime.Shared.CheckConnection(identity)); }
        catch (Exception) { await Notice("Verbindungstest nicht möglich. UseScope-Port und Internetverbindung prüfen."); }
    }
    private async Task<bool> ConfigureGoogle(Identity identity)
    {
        var dialog = new Window { Title = "Google-Projekt verbinden", Width = 550, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var project = new TextBox { Watermark = "Projekt-ID", Text = JsonData.Text(identity.ConnectionOptions?["ProjectId"]) ?? "" };
        var table = new TextBox { Watermark = "Optional: projekt.dataset.tabelle", Text = JsonData.Text(identity.ConnectionOptions?["BillingExport"]) ?? "" };
        var yes = new Button { Content = "Weiter" }; var no = new Button { Content = "Abbrechen" }; var accepted = false;
        yes.Click += (_, _) => { accepted = true; dialog.Close(); }; no.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Thickness(18), Spacing = 12, Children = {
            new TextBlock { Text = "gcloud separat installieren. Eigenes Browserlogin folgt; Monitoring-Leserechte auf dem Projekt erforderlich.", TextWrapping = TextWrapping.Wrap }, project, table,
            new TextBlock { Text = "Nur mit Exporttabelle: SQL-Leseabfrage für Projektkosten, maximal alle 30 Minuten und 100 MiB abgerechnete Daten je Abfrage. Es können BigQuery-Kosten entstehen; Export/IAM selbst einrichten.", TextWrapping = TextWrapping.Wrap },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { no, yes } } } };
        if (!_widget.IsVisible) _widget.Show(); await dialog.ShowDialog(_widget);
        if (!accepted) return false;
        _preferences.SetConnection(identity, ConnectionSetup.Google(project.Text ?? "", table.Text ?? "")); return true;
    }
    private async Task<bool> ConfigureAlibaba(Identity identity)
    {
        var window = new Window { Title = "Alibaba-Plan verbinden", Width = 480, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var site = new ComboBox { ItemsSource = new[] { "International · ap-southeast-1", "China · cn-beijing" }, SelectedIndex = JsonData.Text(identity.ConnectionOptions?["ConsoleSite"]) == "china" ? 1 : 0 };
        var yes = new Button { Content = "Weiter" }; var no = new Button { Content = "Abbrechen" }; var accepted = false;
        yes.Click += (_, _) => { accepted = true; window.Close(); }; no.Click += (_, _) => window.Close();
        window.Content = new StackPanel { Margin = new Thickness(18), Spacing = 14, Children = {
            new TextBlock { Text = "Site deines Plan-Kontos wählen. Login und Messung verwenden dieselbe Site. Internationale Planabdeckung ist mit dem Herstellerkonto zu prüfen.", TextWrapping = TextWrapping.Wrap }, site,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { no, yes } } } };
        if (!_widget.IsVisible) _widget.Show(); await window.ShowDialog(_widget);
        if (!accepted) return false;
        _preferences.SetConnection(identity, new System.Text.Json.Nodes.JsonObject { ["ConsoleSite"] = site.SelectedIndex == 1 ? "china" : "international" }); return true;
    }

}

internal sealed class Widget : Window
{
    private readonly Preferences _preferences;
    private readonly IReadOnlyDictionary<string, Reading> _readings;
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly Border _surface;
    private readonly List<Bitmap> _images = [];
    private readonly Button _orderButton, _pinButton;
    internal Button AddButton { get; }
    internal event Action? Pin, Order, Add;
    internal Widget(Preferences preferences, IReadOnlyDictionary<string, Reading> readings)
    {
        _preferences = preferences; _readings = readings;
        Title = "UseScope"; Width = 400; SizeToContent = SizeToContent.Height; CanResize = false;
        SystemDecorations = SystemDecorations.None; ShowInTaskbar = false;
        var caption = new TextBlock { Text = "Nutzungslimits", FontSize = 12, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        caption.PointerPressed += (_, args) => { if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args); };
        var order = new Button { Content = "☷", Padding = new Thickness(4, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 17 };
        var pin = new Button { Content = "▣", Padding = new Thickness(4, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 17 };
        AddButton = new Button { Content = "+", Padding = new Thickness(4, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 20 };
        Avalonia.Automation.AutomationProperties.SetName(AddButton, "Konto hinzufügen");
        AddButton.Click += (_, _) => Add?.Invoke();
        _orderButton = order; _pinButton = pin;
        order.Click += (_, _) => Order?.Invoke(); pin.Click += (_, _) => Pin?.Invoke();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,26,26,26"), Height = 26 };
        header.Children.Add(caption); Grid.SetColumn(AddButton, 1); header.Children.Add(AddButton); Grid.SetColumn(order, 2); header.Children.Add(order); Grid.SetColumn(pin, 3); header.Children.Add(pin);
        var content = new StackPanel { Spacing = 4, Children = { header, new Border { Height = 1, Background = Brush.Parse("#2B4141") }, new ScrollViewer { Content = _rows, MaxHeight = 650 } } };
        _surface = new Border { Child = content, Padding = new Thickness(12, 0, 12, 6), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
        Content = _surface;
        KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Escape) Hide(); };
        PositionChanged += (_, _) => { if (IsVisible) Dispatcher.UIThread.Post(Clamp); };
    }
    internal void Update()
    {
        var dark = _preferences.Theme == "Dark";
        var ink = dark ? Brush.Parse("#E6EBDD") : Brush.Parse("#262D36");
        var muted = dark ? Brush.Parse("#8CA09A") : Brush.Parse("#7E8690");
        Foreground = ink; Background = dark ? Brush.Parse("#040A0B") : Brush.Parse("#FAFBFC");
        _surface.Background = Background; _surface.BorderBrush = dark ? Brush.Parse("#2B4141") : Brush.Parse("#E1E5EA");
        Topmost = _preferences.Pinned;
        _orderButton.Foreground = ink; _pinButton.Foreground = ink; AddButton.Foreground = ink;
        _rows.Children.Clear(); DisposeImages();
        foreach (var identity in _preferences.Identities.Where(identity => identity.Enabled))
        {
            var reading = _readings.GetValueOrDefault(identity.Key);
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*,128"), Height = 18 };
            var iconFile = Providers.Icon(identity.Vendor, dark);
            var iconPath = iconFile is null ? "" : Path.Combine(AppContext.BaseDirectory, "Assets", iconFile);
            if (File.Exists(iconPath)) { var image = new Bitmap(iconPath); _images.Add(image); header.Children.Add(new Image { Source = image, Width = 13, Height = 13 }); }
            else header.Children.Add(new TextBlock { Text = Providers.Get(identity.Vendor).Badge, FontSize = 12, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
            var name = new TextBlock { Text = reading?.Status.Contains('@') == true ? reading.Status : identity.Title, FontSize = 10, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(name, 1); header.Children.Add(name);
            var wallet = new TextBlock { Text = identity.ShowBalance ? ReadingPresentation.Balance(identity, reading) : "", FontSize = 10, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Right, Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Child = wallet, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = dark ? Brush.Parse("#2B4141") : Brush.Parse("#A0DDD0"), Background = dark ? Brush.Parse("#102324") : Brush.Parse("#EAF9F3") };
            Grid.SetColumn(badge, 2); header.Children.Add(badge);
            _rows.Children.Add(header);
            if (reading?.Periods.Count > 0)
            {
                for (var index = 0; index < reading.Periods.Count; index += 2)
                {
                    var line = new MeterLine(reading.Periods.Skip(index).Take(2).ToArray(), dark, _preferences.ShowRemainingQuota);
                    _rows.Children.Add(line);
                }
            }
            else {
                var detail = new TextBlock { Text = ReadingPresentation.Summary(identity, reading), FontSize = 9, Foreground = muted, TextTrimming = TextTrimming.CharacterEllipsis };
                _rows.Children.Add(detail);
            }
        }
        if (_rows.Children.Count == 0) _rows.Children.Add(new TextBlock { Text = "Konto über + hinzufügen.", FontSize = 11, Margin = new Thickness(0, 8) });
        Dispatcher.UIThread.Post(Clamp);
    }
    internal void Place()
    {
        var screen = Screens.Primary; if (screen is null) return;
        var area = screen.WorkingArea; var scale = screen.Scaling;
        Position = new PixelPoint(area.Right - (int)(Width * scale) - 16, area.Bottom - (int)(Bounds.Height * scale) - 16);
        Clamp();
    }
    private void Clamp()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary; if (screen is null) return;
        var area = screen.WorkingArea; var width = (int)(Bounds.Width * screen.Scaling); var height = (int)(Bounds.Height * screen.Scaling);
        var next = new PixelPoint(Math.Clamp(Position.X, area.X + 16, Math.Max(area.X + 16, area.Right - width - 16)),
            Math.Clamp(Position.Y, area.Y + 16, Math.Max(area.Y + 16, area.Bottom - height - 16)));
        if (next != Position) Position = next;
    }
    internal void DisposeImages() { foreach (var image in _images) image.Dispose(); _images.Clear(); }
}

internal sealed class MeterLine(Period[] periods, bool dark, bool remaining = false) : Control
{
    protected override Size MeasureOverride(Size availableSize) => new(double.IsFinite(availableSize.Width) ? availableSize.Width : 374, 17);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = (Bounds.Width - 8) / 2;
        var ink = dark ? Brush.Parse("#E6EBDD") : Brush.Parse("#262D36");
        var muted = dark ? Brush.Parse("#8CA09A") : Brush.Parse("#7E8690");
        for (var index = 0; index < periods.Length; index++)
        {
            var period = periods[index]; var left = index * (width + 8);
            var time = ReadingPresentation.Countdown(period.Reset);
            var text = new FormattedText(time, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 9, muted) { MaxTextWidth = 71, Trimming = TextTrimming.CharacterEllipsis };
            context.DrawText(text, new Avalonia.Point(left, 2));
            var bar = new Rect(left + 72, 5, Math.Max(1, width - 107), 6);
            context.DrawRectangle(dark ? Brush.Parse("#1D2D2E") : Brush.Parse("#E1E0ED"), null, bar, 3, 3);
            var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative), GradientStops = new GradientStops {
                new GradientStop(Color.Parse("#6EAE89"), 0), new GradientStop(Color.Parse("#84AE85"), .55), new GradientStop(Color.Parse("#B2AC7B"), .7), new GradientStop(Color.Parse("#D59768"), .85), new GradientStop(Color.Parse("#E05B5B"), 1) } };
            if (remaining) { gradient.StartPoint = new RelativePoint(1, .5, RelativeUnit.Relative); gradient.EndPoint = new RelativePoint(0, .5, RelativeUnit.Relative); }
            var shownPercent = ReadingPresentation.QuotaPercent(period, remaining);
            using (context.PushClip(new Rect(bar.X, bar.Y, bar.Width * shownPercent / 100, bar.Height))) context.DrawRectangle(gradient, null, bar, 3, 3);
            var percent = new FormattedText(shownPercent.ToString("0") + "%", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 9, ink);
            context.DrawText(percent, new Avalonia.Point(left + width - percent.Width, 2));
        }
    }
}
