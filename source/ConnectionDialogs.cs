namespace UseScope.Desktop;

internal sealed class ApiCredentialDialog : Form
{
    private readonly Dictionary<string, TextBox> _fields = [];
    internal ApiCredentialDialog(Provider provider, bool meter = false)
    {
        Text = provider.Name; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var definitions = meter ? MeterSetup.Fields(provider.Id) : ConnectionSetup.Fields(provider.Id); ClientSize = new Size(520, 200 + definitions.Count * 58);
        Controls.Add(new Label { Left = 14, Top = 12, Width = 490, Height = 70, Text = meter ? MeterSetup.Help : provider.Help });
        var portal = new Button { Text = "Offizielles Anbieterportal öffnen…", Left = 14, Top = 86, Width = 300 };
        portal.Click += (_, _) => { try { Providers.OpenPortal(provider.Id); } catch { MessageBox.Show("Standardbrowser prüfen.", "UseScope"); } }; Controls.Add(portal);
        var y = 124;
        foreach (var field in definitions)
        {
            Controls.Add(new Label { Text = field.Label, Left = 14, Top = y, Width = 490, Height = 20 });
            var box = new TextBox { Left = 14, Top = y + 22, Width = 490, Text = field.Default, UseSystemPasswordChar = field.Secret, MaxLength = 4096 };
            _fields[field.Id] = box; Controls.Add(box); y += 58;
        }
        Controls.Add(new Label { Text = "Zugangsdaten lokal für deinen Windows-Benutzer verschlüsselt.", Left = 14, Top = y, Width = 490, Height = 30 });
        var save = new Button { Text = "Speichern", Left = 294, Top = y + 34, Width = 100, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Abbrechen", Left = 404, Top = y + 34, Width = 100, DialogResult = DialogResult.Cancel };
        Controls.AddRange([save, cancel]); AcceptButton = save; CancelButton = cancel;
    }
    internal string Encode(string vendor) => ConnectionSetup.Encode(vendor, _fields.ToDictionary(item => item.Key, item => item.Value.Text));
    internal MeterCredential Meter(string? localToken) => MeterSetup.Encode(_fields.ToDictionary(item => item.Key, item => item.Value.Text), localToken);
    protected override void Dispose(bool disposing) { if (disposing) foreach (var field in _fields.Values) field.Clear(); base.Dispose(disposing); }
}

internal sealed class GoogleOptionsDialog : Form
{
    private readonly TextBox _project, _table;
    internal GoogleOptionsDialog(Identity identity)
    {
        Text = "Google-Projekt verbinden"; ClientSize = new Size(540, 294); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        Controls.Add(new Label { Text = "Google-Cloud-CLI (gcloud) erforderlich. Browserlogin folgt. Das Konto braucht Monitoring-Leserechte auf diesem Projekt.", Left = 14, Top = 12, Width = 510, Height = 42 });
        Controls.Add(new Label { Text = "Projekt-ID", Left = 14, Top = 64, Width = 500 });
        _project = new TextBox { Left = 14, Top = 88, Width = 510, Text = JsonData.Text(identity.ConnectionOptions?["ProjectId"]) ?? "" };
        Controls.Add(new Label { Text = "Billing-Exporttabelle optional: projekt.dataset.tabelle", Left = 14, Top = 124, Width = 510 });
        _table = new TextBox { Left = 14, Top = 148, Width = 510, Text = JsonData.Text(identity.ConnectionOptions?["BillingExport"]) ?? "" };
        Controls.Add(new Label { Text = "Nur bei angegebener Exporttabelle: SQL-Leseabfrage für Projektkosten, maximal alle 30 Minuten und bis 100 MiB abgerechnete Daten pro Abfrage. Es können BigQuery-Kosten entstehen. Export/IAM wird nicht automatisch eingerichtet.", Left = 14, Top = 184, Width = 510, Height = 54 });
        var save = new Button { Text = "Weiter", Left = 314, Top = 252, Width = 100, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Abbrechen", Left = 424, Top = 252, Width = 100, DialogResult = DialogResult.Cancel };
        Controls.AddRange([_project, _table, save, cancel]); AcceptButton = save; CancelButton = cancel;
    }
    internal System.Text.Json.Nodes.JsonObject Options() => ConnectionSetup.Google(_project.Text, _table.Text);
}

internal sealed class AlibabaSiteDialog : Form
{
    private readonly ComboBox _site;
    internal AlibabaSiteDialog(Identity identity)
    {
        Text = "Alibaba-Plan verbinden"; ClientSize = new Size(470, 174); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        Controls.Add(new Label { Left = 14, Top = 12, Width = 440, Height = 50, Text = "Wähle die Console-Site deines Plan-Kontos. Login und Messung nutzen dieselbe Site im eigenen Profil. Die internationale Planabdeckung muss mit dem Herstellerkonto geprüft werden." });
        _site = new ComboBox { Left = 14, Top = 70, Width = 440, DropDownStyle = ComboBoxStyle.DropDownList };
        _site.Items.AddRange(["International · ap-southeast-1", "China · cn-beijing"]); _site.SelectedIndex = JsonData.Text(identity.ConnectionOptions?["ConsoleSite"]) == "china" ? 1 : 0;
        var yes = new Button { Text = "Weiter", Left = 244, Top = 126, Width = 100, DialogResult = DialogResult.OK };
        var no = new Button { Text = "Abbrechen", Left = 354, Top = 126, Width = 100, DialogResult = DialogResult.Cancel };
        Controls.AddRange([_site, yes, no]); AcceptButton = yes; CancelButton = no;
    }
    internal System.Text.Json.Nodes.JsonObject Options() => new() { ["ConsoleSite"] = _site.SelectedIndex == 1 ? "china" : "international" };
}
