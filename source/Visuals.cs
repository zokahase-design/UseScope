using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UseScope.Desktop;

internal sealed record Colors(Color Surface, Color Card, Color Ink, Color Muted, Color Line, Color Track)
{
    internal static Colors For(string theme) => theme == "Dark"
        ? new(Color.FromArgb(4, 10, 11), Color.FromArgb(16, 34, 36), Color.FromArgb(230, 235, 221), Color.FromArgb(140, 160, 154), Color.FromArgb(43, 65, 65), Color.FromArgb(29, 45, 46))
        : new(Color.FromArgb(250, 251, 252), Color.FromArgb(250, 251, 252), Color.FromArgb(38, 45, 54), Color.FromArgb(126, 134, 144), Color.FromArgb(225, 229, 234), Color.FromArgb(225, 224, 237));
}
internal static class Artwork
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    internal static Icon CreateIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
    }
    internal static Image? ProviderImage(string vendor, string theme)
    {
        if (vendor == "nous")
        {
            var image = new Bitmap(32, 32);
            using var g = Graphics.FromImage(image); g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(91, 112, 142)); g.FillEllipse(fill, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "N", font, new Rectangle(1, 0, 30, 30), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return image;
        }
        var file = Providers.Icon(vendor, theme == "Dark");
        if (file is null) return null;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", file);
            if (file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) { using var icon = new Icon(path); return icon.ToBitmap(); }
            using var image = Image.FromFile(path); return new Bitmap(image);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException) { return null; }
    }
    internal static string Badge(string vendor) => Providers.Get(vendor).Badge;
    internal static string VendorName(string vendor) => Providers.Get(vendor).Name;
    internal static string Countdown(DateTimeOffset? reset) => ReadingPresentation.Countdown(reset);
    internal static void Text(Graphics graphics, string value, Rectangle box, Color color, Font font, bool right = false)
        => TextRenderer.DrawText(graphics, value, font, box, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
            | (right ? TextFormatFlags.Right : TextFormatFlags.Left));
}
