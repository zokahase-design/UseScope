using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace UseScope.Desktop;

internal sealed class Dashboard : Form
{
    private readonly Preferences _preferences;
    private readonly Dictionary<(string, string), Image?> _images = new();
    private IReadOnlyDictionary<string, Reading> _readings = new Dictionary<string, Reading>();
    private readonly List<(string Key, Rectangle Bounds)> _sections = [];
    private bool _menuActive, _shellOverlayActive, _arranging;
    private int _scroll, _fullHeight;
    private string? _dragged, _target;
    internal Rectangle AddButton => new(ClientSize.Width - 90, 2, 24, 23);
    private Rectangle OrderButton => new(ClientSize.Width - 62, 2, 28, 23);
    private Rectangle PinButton => new(ClientSize.Width - 30, 2, 24, 23);
    internal event Action? PinRequested, OrderChanged, AddRequested;
    protected override bool ShowWithoutActivation => true;
    internal bool IsInForeground => IsHandleCreated && GetForegroundWindow() == Handle;
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
            try { preferences.Save(); } catch (Exception) { System.Diagnostics.Trace.WriteLine("UseScope: Position konnte nicht gespeichert werden."); }
        };
    }
    internal void MenuActive(bool active)
    {
        _menuActive = active;
        ApplyForegroundPriority();
    }
    internal void ShellOverlayActive(bool active)
    {
        _shellOverlayActive = active;
        ApplyForegroundPriority();
    }
    private void ApplyForegroundPriority()
    {
        var desired = _preferences.Pinned && !_menuActive && !_shellOverlayActive;
        // Reapplying an unchanged TopMost value can reorder the widget above the Windows tray flyout.
        if (TopMost != desired) TopMost = desired;
    }
    internal void RaiseForUser()
    {
        if (_menuActive || _shellOverlayActive || !Visible) return;
        Activate();
        if (TopMost) return;

        // Keep the one-time raise separate from the persistent pin preference.
        const uint noSize = 0x0001, noMove = 0x0002, noActivate = 0x0010, noOwnerZOrder = 0x0200;
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, noSize | noMove | noActivate | noOwnerZOrder);
    }
    internal void Render(IReadOnlyDictionary<string, Reading> readings)
    {
        _readings = readings;
        ApplyForegroundPriority();
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
        Artwork.Text(g, "Nutzungslimits", new Rectangle(12, 3, Math.Max(1, AddButton.Left - 16), 20), palette.Ink, heading);
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
                var balance = ReadingPresentation.Balance(identity, reading);
                Artwork.Text(g, balance, new Rectangle(badgeX + 6, y, 116, 16), palette.Ink, numbers, true);
            }
            if (reading?.Periods.Count > 0)
                for (var index = 0; index < reading.Periods.Count; index++)
                    Meter(g, reading.Periods[index], index % 2, y + 18 + index / 2 * 17, palette, numbers, small, reading.Failed);
            else Artwork.Text(g, ReadingPresentation.Summary(identity, reading), new Rectangle(16, y + 18, Math.Max(1, Width - 32), 17), palette.Muted, small);
            if (reading?.Failed == true) Artwork.Text(g, "!", new Rectangle(Width - 10, y, 8, 16), Color.DarkOrange, numbers);
            y += height;
            g.DrawLine(border, 12, y - 2, Width - 12, y - 2);
        }
        if (_sections.Count == 0) Artwork.Text(g, "Konto über + hinzufügen.", new Rectangle(12, 34, Width - 24, 24), palette.Muted, small);
        g.Restore(saved);
    }
    private void Meter(Graphics g, Period period, int column, int y, Colors colors, Font numbers, Font small, bool stale)
    {
        var span = (Width - 40) / 2;
        var x = 16 + column * (span + 8);
        Artwork.Text(g, Artwork.Countdown(period.Reset), new Rectangle(x, y, 72, 17), colors.Muted, small);
        var track = new Rectangle(x + 72, y + 5, Math.Max(1, span - 107), 6);
        using var shape = Rounded(track, 3); using var background = new SolidBrush(colors.Track); g.FillPath(background, shape);
        using var gradient = new LinearGradientBrush(track, Color.FromArgb(110, 174, 137), Color.FromArgb(224, 91, 91), 0f);
        gradient.InterpolationColors = new ColorBlend { Positions = [0, .55f, .70f, .85f, 1], Colors = [Color.FromArgb(110, 174, 137), Color.FromArgb(132, 174, 133), Color.FromArgb(178, 172, 123), Color.FromArgb(213, 151, 104), Color.FromArgb(224, 91, 91)] };
        if (_preferences.ShowRemainingQuota)
        {
            var blend = gradient.InterpolationColors;
            gradient.InterpolationColors = new ColorBlend { Positions = blend.Positions.Reverse().Select(position => 1 - position).ToArray(), Colors = blend.Colors.Reverse().ToArray() };
        }
        var percent = ReadingPresentation.QuotaPercent(period, _preferences.ShowRemainingQuota);
        var state = g.Save(); g.SetClip(shape, CombineMode.Intersect);
        g.IntersectClip(new Rectangle(track.X, track.Y, (int)Math.Round(track.Width * percent / 100), track.Height));
        g.FillRectangle(gradient, track); g.Restore(state);
        Artwork.Text(g, $"{percent:0}%", new Rectangle(x + span - 28, y, 28, 17), stale ? colors.Muted : colors.Ink, numbers, true);
    }
    private void DrawButtons(Graphics g, Colors colors)
    {
        using var plus = new Pen(colors.Ink, 1.5f);
        var center = new Point(AddButton.X + AddButton.Width / 2, AddButton.Y + AddButton.Height / 2);
        g.DrawLine(plus, center.X - 5, center.Y, center.X + 5, center.Y);
        g.DrawLine(plus, center.X, center.Y - 5, center.X, center.Y + 5);
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
        if (ActivateHeader(args.Location)) return;
        if (_arranging && args.Y >= 29)
        { _dragged = _sections.FirstOrDefault(item => item.Bounds.Contains(args.Location)).Key; Capture = _dragged is not null; return; }
        if (args.Y < 28) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
    }
    internal bool ActivateHeader(Point location)
    {
        if (AddButton.Contains(location)) { AddRequested?.Invoke(); return true; }
        if (OrderButton.Contains(location)) { _arranging = !_arranging; _dragged = _target = null; Invalidate(); return true; }
        if (PinButton.Contains(location)) { PinRequested?.Invoke(); return true; }
        return false;
    }
    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (_dragged is not null)
        { _target = _sections.FirstOrDefault(item => item.Bounds.Contains(args.Location)).Key; Cursor = Cursors.SizeAll; Invalidate(); return; }
        Cursor = AddButton.Contains(args.Location) || OrderButton.Contains(args.Location) || PinButton.Contains(args.Location) ? Cursors.Hand
            : _arranging && args.Y >= 29 ? Cursors.SizeAll : Cursors.Default;
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
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    protected override void Dispose(bool disposing)
    { if (disposing) { foreach (var image in _images.Values) image?.Dispose(); Icon?.Dispose(); Font.Dispose(); } base.Dispose(disposing); }
}
