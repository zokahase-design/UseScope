using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace UseScope.Desktop;

internal sealed class Dashboard : Form
{
    private readonly Preferences _preferences;
    private readonly ToolTip _tips = new();
    private readonly Dictionary<(string, string), Image?> _images = new();
    private IReadOnlyDictionary<string, Reading> _readings = new Dictionary<string, Reading>();
    private readonly List<(string Key, Rectangle Bounds)> _sections = [];
    private bool _menuActive, _arranging;
    private int _scroll, _fullHeight;
    private string? _dragged, _target;
    private Rectangle OrderButton => new(ClientSize.Width - 62, 2, 28, 23);
    private Rectangle PinButton => new(ClientSize.Width - 30, 2, 24, 23);
    internal event Action? PinRequested, OrderChanged;
    internal Dashboard(Preferences preferences)
    {
        _preferences = preferences;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        Text = "UseScope";
        ClientSize = new Size(400, 100);
        Font = new Font("Segoe UI", 8f * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        Icon = Artwork.CreateIcon();
        DoubleBuffered = true; KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };
        ResizeEnd += (_, _) => {
            Location = Platform.Fit(Location, Size, Screen.FromRectangle(Bounds).WorkingArea);
            preferences.Remember(Location);
            try { preferences.Save(); } catch (Exception) { _tips.SetToolTip(this, "Position konnte nicht gespeichert werden."); }
        };
        MouseLeave += (_, _) => _tips.SetToolTip(this, "");
    }
    internal void MenuActive(bool active)
    {
        _menuActive = active;
        TopMost = _preferences.Pinned && !active;
    }
    internal void Render(IReadOnlyDictionary<string, Reading> readings)
    {
        _readings = readings;
        MenuActive(_menuActive);
        var palette = Colors.For(_preferences.Theme);
        BackColor = palette.Surface; ForeColor = palette.Ink;
        var identities = _preferences.Identities.Where(item => item.Enabled).ToArray();
        _fullHeight = 36 + (identities.Length == 0 ? 30 : identities.Sum(identity => {
            var lines = QuotaLines(readings.GetValueOrDefault(identity.Key)); return 16 + (lines > 0 ? 2 + lines * 17 : 18);
        }) + Math.Max(0, identities.Length - 1) * 4);
        var area = Screen.FromRectangle(Bounds).WorkingArea;
        ClientSize = new Size(Math.Min(400, Math.Max(1, area.Width - 32)), Math.Min(_fullHeight, Math.Max(32, area.Height - 32)));
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _fullHeight - ClientSize.Height));
        Location = Platform.Fit(Location, Size, area);
        using var outline = Rounded(new Rectangle(0, 0, Width, Height), 10);
        var old = Region; Region = new Region(outline); old?.Dispose();
        Invalidate();
    }
    private static int QuotaLines(Reading? reading) => reading?.Periods.Count > 0 ? (reading.Periods.Count + 1) / 2 : 0;
    protected override void OnPaint(PaintEventArgs args)
    {
        var g = args.Graphics; var palette = Colors.For(_preferences.Theme);
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (_preferences.Theme == "Dark")
        { using var paint = new LinearGradientBrush(ClientRectangle, palette.Surface, palette.Card, 12); g.FillRectangle(paint, ClientRectangle); }
        else g.Clear(palette.Surface);
        using var border = new Pen(palette.Line);
        using var outline = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 10); g.DrawPath(border, outline);
        using var heading = new Font(Font.FontFamily, 8.6f * 96f / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var account = new Font(Font.FontFamily, 7.6f * 96f / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var numbers = new Font(Font.FontFamily, 7.2f * 96f / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var small = new Font(Font.FontFamily, 7.2f * 96f / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        Artwork.Text(g, "Nutzungslimits", new Rectangle(12, 3, 250, 20), palette.Ink, heading);
        DrawButtons(g, palette);
        g.DrawLine(border, 12, 27, Width - 12, 27);
        var saved = g.Save(); g.SetClip(new Rectangle(1, 29, Width - 2, Math.Max(0, Height - 30)));
        _sections.Clear(); var y = 31 - _scroll;
        foreach (var identity in _preferences.Identities.Where(item => item.Enabled))
        {
            var reading = _readings.GetValueOrDefault(identity.Key);
            var lines = QuotaLines(reading); var height = 16 + (lines > 0 ? 2 + lines * 17 : 18) + 4;
            var bounds = new Rectangle(8, y, Width - 16, height);
            _sections.Add((identity.Key, bounds));
            if (_arranging && (_target == identity.Key || _dragged == identity.Key))
            { using var selected = new SolidBrush(Color.FromArgb(32, 70, 190, 150)); g.FillRectangle(selected, bounds); }
            var imageKey = (identity.Vendor, _preferences.Theme);
            if (!_images.TryGetValue(imageKey, out var image)) { image = Artwork.ProviderImage(identity.Vendor, _preferences.Theme); _images[imageKey] = image; }
            if (image is not null) g.DrawImage(image, new Rectangle(12, y + 1, 13, 13));
            else Artwork.Text(g, Artwork.Badge(identity.Vendor), new Rectangle(12, y, 14, 16), palette.Ink, numbers);
            var title = reading?.Status.Contains('@') == true ? reading.Status : identity.Title;
            var badgeX = Width - 140;
            Artwork.Text(g, title, new Rectangle(30, y, Math.Max(1, (identity.ShowBalance ? badgeX - 13 : Width - 12) - 30), 16), palette.Ink, account);
            if (identity.ShowBalance)
            {
                var badge = new Rectangle(badgeX, y, 128, 16);
                using var shape = Rounded(badge, 4);
                var available = reading?.Balance is not null;
                var tint = available ? _preferences.Theme == "Dark" ? Color.FromArgb(116, 172, 156) : Color.FromArgb(0, 225, 163) : palette.Muted;
                using var fill = new SolidBrush(Color.FromArgb(available ? 21 : 12, tint));
                using var line = new Pen(Color.FromArgb(available ? 75 : 40, tint));
                g.FillPath(fill, shape); g.DrawPath(line, shape);
                using var dot = new SolidBrush(reading?.Observed is not null && !reading.Failed ? Color.FromArgb(0, 225, 163) : palette.Muted); g.FillEllipse(dot, badgeX - 9, y + 6, 4, 4);
                var balance = reading?.Balance ?? "—";
                if (reading?.Balance is not null && identity.Vendor == "claude") balance = "Guthaben " + balance;
                Artwork.Text(g, balance, new Rectangle(badgeX + 6, y, 116, 16), palette.Ink, numbers, true);
            }
            if (reading?.Periods.Count > 0)
                for (var index = 0; index < reading.Periods.Count; index++)
                    Meter(g, reading.Periods[index], index % 2, y + 18 + index / 2 * 17, palette, numbers, small, reading.Failed);
            else Artwork.Text(g, ReadingPresentation.Summary(identity, reading), new Rectangle(16, y + 18, Width - 32, 17), palette.Muted, small);
            if (reading?.Failed == true) Artwork.Text(g, "!", new Rectangle(Width - 10, y, 8, 16), Color.DarkOrange, numbers);
            y += height;
            g.DrawLine(border, 12, y - 2, Width - 12, y - 2);
        }
        if (_sections.Count == 0) Artwork.Text(g, "Konten über das Tray-Menü hinzufügen.", new Rectangle(12, 34, Width - 24, 24), palette.Muted, small);
        g.Restore(saved);
    }
    private void Meter(Graphics g, Period period, int column, int y, Colors colors, Font numbers, Font small, bool stale)
    {
        var span = (Width - 40) / 2;
        var x = 16 + column * (span + 8);
        Artwork.Text(g, Artwork.Countdown(period.Reset), new Rectangle(x, y, 58, 17), colors.Muted, small);
        var track = new Rectangle(x + 58, y + 5, Math.Max(1, span - 93), 6);
        using var shape = Rounded(track, 3); using var background = new SolidBrush(colors.Track); g.FillPath(background, shape);
        using var gradient = new LinearGradientBrush(track, Color.FromArgb(110, 174, 137), Color.FromArgb(224, 91, 91), 0f);
        gradient.InterpolationColors = new ColorBlend { Positions = [0, .55f, .70f, .85f, 1], Colors = [Color.FromArgb(110, 174, 137), Color.FromArgb(132, 174, 133), Color.FromArgb(178, 172, 123), Color.FromArgb(213, 151, 104), Color.FromArgb(224, 91, 91)] };
        var state = g.Save(); g.SetClip(shape, CombineMode.Intersect);
        g.IntersectClip(new Rectangle(track.X, track.Y, (int)Math.Round(track.Width * period.Percent / 100), track.Height));
        g.FillRectangle(gradient, track); g.Restore(state);
        Artwork.Text(g, $"{period.Percent:0}%", new Rectangle(x + span - 28, y, 28, 17), stale ? colors.Muted : colors.Ink, numbers, true);
    }
    private void DrawButtons(Graphics g, Colors colors)
    {
        using var pen = new Pen(_arranging ? Color.FromArgb(20, 190, 150) : colors.Muted, 1.4f);
        for (var row = 0; row < 3; row++) { var y = 7 + row * 6; g.DrawEllipse(pen, OrderButton.X + 8, y, 1, 1); g.DrawLine(pen, OrderButton.X + 13, y + .5f, OrderButton.X + 21, y + .5f); }
        using var lockPen = new Pen(_preferences.Pinned ? colors.Ink : colors.Muted, 1.6f);
        var x = PinButton.X + 6;
        g.DrawArc(lockPen, x + 2, 5, 7, 9, 180, 180);
        g.DrawRectangle(lockPen, x, 10, 11, 9); g.DrawLine(lockPen, x + 5.5f, 13, x + 5.5f, 17);
    }
    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args); if (args.Button != MouseButtons.Left) return;
        if (OrderButton.Contains(args.Location)) { _arranging = !_arranging; _dragged = _target = null; Invalidate(); return; }
        if (PinButton.Contains(args.Location)) { PinRequested?.Invoke(); return; }
        if (_arranging && args.Y >= 29)
        { _dragged = _sections.FirstOrDefault(item => item.Bounds.Contains(args.Location)).Key; Capture = _dragged is not null; return; }
        if (args.Y < 28) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
    }
    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (_dragged is not null)
        { _target = _sections.FirstOrDefault(item => item.Bounds.Contains(args.Location)).Key; Cursor = Cursors.SizeAll; Invalidate(); return; }
        Cursor = _arranging && args.Y >= 29 ? Cursors.SizeAll : Cursors.Default;
        string tip;
        if (OrderButton.Contains(args.Location)) tip = _arranging ? "Konten verschieben: aktiv" : "Konten-Reihenfolge ändern";
        else if (PinButton.Contains(args.Location)) tip = "Immer im Vordergrund";
        else
        {
            var key = _sections.FirstOrDefault(item => item.Bounds.Contains(args.Location)).Key;
            var reading = key is not null ? _readings.GetValueOrDefault(key) : null;
            tip = reading?.Status ?? "";
            var identity = _preferences.Identities.FirstOrDefault(item => item.Key == key);
            if (identity is not null) tip += "\n" + ReadingPresentation.Detail(identity, reading);
            if (reading?.Observed is DateTimeOffset instant) tip += "\nStand: " + instant.ToLocalTime().ToString("dd.MM. HH:mm:ss");
            if (reading?.Failed == true) tip += "\nLetzte bekannte Werte; aktuelle Abfrage fehlgeschlagen.";
            if (reading?.Periods.Count > 0) tip += "\n" + string.Join(" · ", reading.Periods.Select(period => period.Caption));
        }
        _tips.SetToolTip(this, tip);
    }
    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        var moved = _dragged; var target = _target;
        _dragged = _target = null; Capture = false;
        if (moved is not null && target is not null && moved != target)
        {
            try { _preferences.Reorder(moved, target); OrderChanged?.Invoke(); }
            catch (Exception) { MessageBox.Show(this, "Die Kontoreihenfolge konnte nicht gespeichert werden. Die bisherige Reihenfolge bleibt erhalten.", "UseScope", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs args)
    { base.OnMouseWheel(args); _scroll = Math.Clamp(_scroll - args.Delta / 4, 0, Math.Max(0, _fullHeight - Height)); Invalidate(); }
    internal void Place()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var requested = _preferences.Position switch {
            "LastPosition" => _preferences.LastLocation ?? Location,
            "TopLeft" => new Point(area.Left + 16, area.Top + 16),
            "TopRight" => new Point(area.Right - Width - 16, area.Top + 16),
            "BottomLeft" => new Point(area.Left + 16, area.Bottom - Height - 16),
            "Center" => new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2),
            "NearCursor" => Cursor.Position + new Size(14, 14),
            _ => new Point(area.Right - Width - 16, area.Bottom - Height - 16)
        };
        Location = Platform.Fit(requested, Size, area);
    }
    private static GraphicsPath Rounded(Rectangle box, float radius)
    {
        var path = new GraphicsPath(); var diameter = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        if (diameter <= 0) { path.AddRectangle(box); return path; }
        path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
        path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
    protected override void Dispose(bool disposing)
    { if (disposing) { _tips.Dispose(); foreach (var image in _images.Values) image?.Dispose(); Icon?.Dispose(); Font.Dispose(); } base.Dispose(disposing); }
}
