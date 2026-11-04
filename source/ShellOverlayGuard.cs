using System.Runtime.InteropServices;
using System.Text;

namespace UseScope.Desktop;

// Observes shell window classes/visibility only. Never reads titles or controls another application's windows.
internal sealed class ShellOverlayGuard : IDisposable
{
    private readonly Control _owner;
    private readonly Action<bool> _changed;
    private readonly Func<bool> _probe;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 200 };
    private readonly WinEventProc _callback;
    private readonly List<IntPtr> _hooks = [];
    private bool _disposed;
    private int _queued;
    internal bool IsOpen { get; private set; }
    internal ShellOverlayGuard(Control owner, Action<bool> changed, Func<bool>? probe = null)
    {
        _owner = owner; _changed = changed; _probe = probe ?? ProbeShell;
        _callback = OnShellEvent;
        if (probe is not null) return; // Offline checks inject state; they never observe the real desktop.
        foreach (var (first, last) in new[] { (0x0003u, 0x0003u), (0x8002u, 0x8003u) })
        {
            var hook = SetWinEventHook(first, last, IntPtr.Zero, _callback, 0, 0, 0x0002); // Out of context, skip own process.
            if (hook != IntPtr.Zero) _hooks.Add(hook);
        }
        _timer.Tick += (_, _) => Poll(); _timer.Start();
    }
    internal void Poll()
    {
        if (_disposed) return;
        var open = _probe();
        if (open == IsOpen) return;
        IsOpen = open; _changed(open);
    }
    private void OnShellEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint timestamp)
    {
        if (_disposed || (eventType != 0x0003 && (objectId != 0 || childId != 0 || !IsOverflowClass(ClassOf(window))))) return;
        if (Interlocked.Exchange(ref _queued, 1) != 0) return;
        try { _owner.BeginInvoke((Action)(() => { Interlocked.Exchange(ref _queued, 0); if (!_disposed) Poll(); })); }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _queued, 0); }
    }
    internal static bool IsOverflowClass(string name) => name is "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland";
    private static bool ProbeShell()
    {
        var open = false;
        EnumWindows((window, _) => {
            if (!IsOverflowClass(ClassOf(window)) || !IsWindowVisible(window)) return true;
            if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            open = true; return false;
        }, IntPtr.Zero);
        return open;
    }
    private static string ClassOf(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";
        var name = new StringBuilder(256); return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _timer.Dispose();
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
    }
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint timestamp);
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
