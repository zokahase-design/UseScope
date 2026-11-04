namespace UseScope.Desktop;

internal static class AppInfo
{
    internal static string Version => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "?";
    internal static string MenuTitle => "UseScope " + Version;
}
