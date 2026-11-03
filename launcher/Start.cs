using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class Start
{
    [STAThread]
    private static int Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string directory = Path.Combine(root, "runtime");
        string executable = Path.Combine(directory, "UseScope.exe");
        if (args.Length == 2 && args[0] == "--check-target")
        {
            File.WriteAllText(args[1], executable, new UTF8Encoding(false));
            return File.Exists(executable) ? 0 : 1;
        }
        if (!File.Exists(executable))
        {
            MessageBox.Show("UseScope wurde nicht vollständig gefunden. Bitte das gesamte ZIP entpacken und den Unterordner runtime beibehalten.", "UseScope", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
        try
        {
            ProcessStartInfo launch = new ProcessStartInfo(executable);
            launch.WorkingDirectory = directory;
            launch.UseShellExecute = false;
            Process process = Process.Start(launch);
            if (process != null) process.Dispose();
            return 0;
        }
        catch (Exception)
        {
            MessageBox.Show("UseScope konnte nicht gestartet werden. Bitte die vollständige ZIP-Datei erneut entpacken und Windows-Kompatibilität prüfen.", "UseScope", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
