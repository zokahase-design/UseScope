using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace UseScope.Desktop;

internal static class Platform
{
    internal static ProcessStartInfo Cli(string vendor, string profile, bool interactive, params string[] arguments)
    {
        var name = vendor == "codex" ? "codex" : vendor == "claude" ? "claude" : "hermes";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new[] { Path.Combine(home, ".local", "bin"), Path.Combine(home, ".hermes", "venv", "bin"), "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)).Distinct();
        var executable = folders.Where(path => path.Length > 0).Select(path => Path.Combine(path, name)).FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(name + " ist nicht installiert oder nicht im Suchpfad.");
        var result = new ProcessStartInfo(executable) { WorkingDirectory = home, UseShellExecute = false,
            RedirectStandardInput = !interactive, RedirectStandardOutput = !interactive, RedirectStandardError = !interactive };
        foreach (var argument in arguments) result.ArgumentList.Add(argument);
        result.Environment[vendor == "codex" ? "CODEX_HOME" : vendor == "claude" ? "CLAUDE_CONFIG_DIR" : "HERMES_HOME"] = profile;
        if (vendor == "claude") result.Environment["CLAUDE_SECURESTORAGE_CONFIG_DIR"] = profile;
        foreach (var variable in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" }) result.Environment.Remove(variable);
        return result;
    }
    internal static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
    internal static string KeychainService(string profile) => "Claude Code-credentials-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.Normalize(NormalizationForm.FormC)))).ToLowerInvariant()[..8];
    internal static async Task Login(Identity identity, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(identity.Profile);
        var args = identity.Vendor == "codex" ? new[] { "-c", "cli_auth_credentials_store='file'", "login" }
            : identity.Vendor == "claude" ? new[] { "auth", "login" } : new[] { "auth", "add", "nous", "--type", "oauth" };
        var cli = Cli(identity.Vendor, identity.Profile, true, args);
        var variable = identity.Vendor == "codex" ? "CODEX_HOME" : identity.Vendor == "claude" ? "CLAUDE_CONFIG_DIR" : "HERMES_HOME";
        // Use a temporary script for terminal I/O, never place credentials in it.
        var script = Path.Combine(Path.GetTempPath(), "usescope-login-" + Guid.NewGuid().ToString("N") + ".command");
        var marker = script + ".done";
        var cancelled = script + ".cancel";
        var owner = Environment.ProcessId;
        using var ownerQuery = Process.Start(new ProcessStartInfo("/bin/ps") {
            ArgumentList = { "-p", owner.ToString(), "-o", "lstart=" }, RedirectStandardOutput = true, UseShellExecute = false })!;
        var ownerStamp = (await ownerQuery.StandardOutput.ReadToEndAsync(cancellation)).Trim();
        await ownerQuery.WaitForExitAsync(cancellation);
        if (ownerStamp.Length == 0) throw new IOException("Anmeldung konnte nicht abgesichert werden.");
        var content = "#!/bin/sh\nparent=$$\nchild=\nwatcher=\nresult=125\n";
        content += "stop_branch() (\n branch=$1\n expected=$2\n test \"$(/bin/ps -p \"$branch\" -o ppid= | /usr/bin/tr -d ' ')\" = \"$expected\" || return\n kill -STOP \"$branch\" 2>/dev/null || return\n for nested in $(/usr/bin/pgrep -P \"$branch\"); do stop_branch \"$nested\" \"$branch\"; done\n if test \"$(/bin/ps -p \"$branch\" -o ppid= | /usr/bin/tr -d ' ')\" = \"$expected\"; then\n kill -TERM \"$branch\" 2>/dev/null\n kill -CONT \"$branch\" 2>/dev/null\n sleep 0.25\n if test \"$(/bin/ps -p \"$branch\" -o ppid= | /usr/bin/tr -d ' ')\" = \"$expected\"; then kill -KILL \"$branch\" 2>/dev/null; fi\n fi\n)\nstop_child() { if test -n \"$child\"; then stop_branch \"$child\" \"$parent\"; fi; }\n";
        content += "finish() {\n if test -n \"$watcher\"; then kill \"$watcher\" 2>/dev/null; wait \"$watcher\" 2>/dev/null; fi\n printf '%s' \"$result\" > " + ShellQuote(marker + ".tmp") + "\n mv " + ShellQuote(marker + ".tmp") + " " + ShellQuote(marker) + "\n}\ntrap finish EXIT\ntrap 'stop_child; wait \"$child\" 2>/dev/null; result=130; exit 130' TERM INT HUP\n";
        content += "if test -f " + ShellQuote(cancelled) + "; then exit 125; fi\n";
        content += "export " + variable + "=" + ShellQuote(identity.Profile) + "\n";
        if (identity.Vendor == "claude") content += "export CLAUDE_SECURESTORAGE_CONFIG_DIR=" + ShellQuote(identity.Profile) + "\n";
        content += "unset OPENAI_API_KEY CODEX_API_KEY ANTHROPIC_API_KEY ANTHROPIC_AUTH_TOKEN CLAUDE_CODE_OAUTH_TOKEN\n";
        content += ShellQuote(cli.FileName) + " " + string.Join(" ", args.Select(ShellQuote)) + " < /dev/tty &\nchild=$!\n";
        content += "( while kill -0 \"$child\" 2>/dev/null; do\n if test -f " + ShellQuote(cancelled) + " || test \"$(/bin/ps -p " + owner + " -o lstart= | /usr/bin/sed 's/^ *//;s/ *$//')\" != " + ShellQuote(ownerStamp) + "; then stop_child; break; fi\n sleep 0.25\n done ) &\nwatcher=$!\nwait \"$child\"\nresult=$?\nexit \"$result\"\n";
        await File.WriteAllTextAsync(script, content, cancellation);
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-a", "Terminal", script }, UseShellExecute = false })!;
            await process.WaitForExitAsync(cancellation);
            if (process.ExitCode != 0) throw new IOException("Terminal konnte nicht geöffnet werden.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation); deadline.CancelAfter(TimeSpan.FromMinutes(15));
            while (!File.Exists(marker)) await Task.Delay(500, deadline.Token);
            if ((await File.ReadAllTextAsync(marker, cancellation)).Trim() != "0") throw new IOException("Anmeldung nicht abgeschlossen.");
        }
        finally
        {
            var completed = File.Exists(marker);
            if (!completed)
            {
                // The wrapper owns and reaps its child, including after app exit.
                // A late Terminal launch sees the sentinel before starting a CLI.
                await File.WriteAllTextAsync(cancelled, "cancel");
                var until = DateTimeOffset.UtcNow.AddSeconds(10);
                while (!File.Exists(marker) && DateTimeOffset.UtcNow < until) await Task.Delay(100);
                completed = File.Exists(marker);
            }
            // If Terminal has not started yet, leave the script and cancellation
            // sentinel in place. Deleting them would permit a delayed auth race.
            if (completed)
                foreach (var file in new[] { script, marker, marker + ".tmp", cancelled }) if (File.Exists(file)) File.Delete(file);
        }
    }
}

internal static class CredentialAccess
{
    internal static async Task<JsonObject?> Claude(string profile, CancellationToken cancellation)
    {
        // Read only the exact selected profile's slot; never fall back to another account.
        if (OperatingSystem.IsMacOS())
        {
            var start = new ProcessStartInfo("/usr/bin/security") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { "find-generic-password", "-s", Platform.KeychainService(profile), "-w" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(cancellation); var error = process.StandardError.ReadToEndAsync(cancellation);
            try
            {
                await process.WaitForExitAsync(cancellation); await error;
                if (process.ExitCode == 0) return (JsonNode.Parse((await output).Trim()) as JsonObject)?["claudeAiOauth"] as JsonObject;
                await output;
            }
            finally { if (!process.HasExited) process.Kill(); }
        }
        var file = Path.Combine(profile, ".credentials.json");
        return File.Exists(file) ? (JsonNode.Parse(await File.ReadAllTextAsync(file, cancellation)) as JsonObject)?["claudeAiOauth"] as JsonObject : null;
    }
}
