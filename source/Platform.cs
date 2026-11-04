using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UseScope.Desktop;

internal static class Platform
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool StartsWithWindows
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(StartupKey); return key?.GetValue("UseScope") is string; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
            if (value) key.SetValue("UseScope", "\"" + StartupExecutable + "\"");
            else key.DeleteValue("UseScope", false);
        }
    }
    internal static string StartupExecutable
    {
        get
        {
            var directory = Path.GetDirectoryName(Application.ExecutablePath)!;
            if (Path.GetFileName(directory).Equals("runtime", StringComparison.OrdinalIgnoreCase))
            {
                var starter = Path.Combine(Path.GetDirectoryName(directory)!, "tools", "UseScope starten.exe");
                if (File.Exists(starter)) return starter;
            }
            return Application.ExecutablePath;
        }
    }
    internal static ProcessStartInfo Cli(string vendor, string profile, bool interactive, params string[] arguments)
    {
        var name = NativeConnections.Executable(vendor);
        var candidates = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", name + ".exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", name + ".cmd")
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => path.Length > 0).SelectMany(path => new[] { ".exe", ".cmd", ".bat" }.Select(extension => Path.Combine(path.Trim('"'), name + extension))));
        var executable = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException($"{name} ist nicht installiert.");
        var result = new ProcessStartInfo { WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false, CreateNoWindow = !interactive, RedirectStandardInput = !interactive,
            RedirectStandardOutput = !interactive, RedirectStandardError = !interactive };
        if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            if (arguments.Any(value => value.Any(c => "\"&|<>^%!\r\n".Contains(c)))) throw new ArgumentException("Ungültiges CLI-Argument.");
            result.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            result.Arguments = "/d /s /c \"\"" + executable + "\" " + string.Join(" ", arguments.Select(value => "\"" + value + "\"")) + "\"";
        }
        else
        {
            result.FileName = executable;
            foreach (var argument in arguments) result.ArgumentList.Add(argument);
        }
        NativeConnections.Isolate(result, vendor, profile);
        return result;
    }
    internal static Point Fit(Point requested, Size size, Rectangle area)
    {
        var insetX = Math.Min(16, Math.Max(0, (area.Width - size.Width) / 2));
        var insetY = Math.Min(16, Math.Max(0, (area.Height - size.Height) / 2));
        var x = Math.Clamp(requested.X, area.Left + insetX, Math.Max(area.Left + insetX, area.Right - size.Width - insetX));
        var y = Math.Clamp(requested.Y, area.Top + insetY, Math.Max(area.Top + insetY, area.Bottom - size.Height - insetY));
        return new Point(x, y);
    }
}

internal sealed class ShortcutWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    private readonly Action _pressed;
    internal bool Registered { get; }
    internal ShortcutWindow(string definition, Action pressed)
    {
        _pressed = pressed;
        CreateHandle(new CreateParams { Caption = "UseScope hotkey" });
        uint modifiers = 0x4000;
        Keys key = Keys.None;
        foreach (var part in definition.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= 2; break;
                case "alt": modifiers |= 1; break;
                case "shift": modifiers |= 4; break;
                case "win": modifiers |= 8; break;
                default: if (!Enum.TryParse(part, true, out key)) key = Keys.None; break;
            }
        Registered = key != Keys.None && RegisterHotKey(Handle, 1, modifiers, (uint)key);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312) _pressed();
        base.WndProc(ref message);
    }
    public void Dispose() { if (Registered) UnregisterHotKey(Handle, 1); DestroyHandle(); }
}
