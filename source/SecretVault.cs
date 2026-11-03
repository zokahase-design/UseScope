using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UseScope.Desktop;

// DPAPI is scoped to this Windows user and to the selected account, never machine-wide.
internal static class SecretVault
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static string FileName(Identity account) => Path.Combine(account.Profile, "api-key.dpapi");
    private static byte[] Transform(byte[] data, Identity account, bool protect)
    {
        var entropy = SHA256.HashData(Encoding.UTF8.GetBytes("UseScope/" + account.Vendor + "/" + account.Key));
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        var extra = new Blob { Length = entropy.Length, Data = Marshal.AllocHGlobal(entropy.Length) };
        var result = default(Blob);
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length); Marshal.Copy(entropy, 0, extra.Data, entropy.Length);
            var success = protect ? CryptProtectData(ref input, null, ref extra, IntPtr.Zero, IntPtr.Zero, 1, out result)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, 1, out result);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var output = new byte[result.Length]; Marshal.Copy(result.Data, output, 0, output.Length); return output;
        }
        finally
        {
            if (result.Data != IntPtr.Zero) { for (var index = 0; index < result.Length; index++) Marshal.WriteByte(result.Data, index, 0); LocalFree(result.Data); }
            for (var index = 0; index < input.Length; index++) Marshal.WriteByte(input.Data, index, 0);
            Marshal.FreeHGlobal(input.Data); Marshal.FreeHGlobal(extra.Data); CryptographicOperations.ZeroMemory(data);
        }
    }
    internal static Task<string?> Read(Identity account, CancellationToken cancellation) => Task.Run(() => {
        cancellation.ThrowIfCancellationRequested(); var file = FileName(account); if (!File.Exists(file)) return null;
        var bytes = Transform(File.ReadAllBytes(file), account, false);
        try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }, cancellation);
    internal static Task Write(Identity account, string value, CancellationToken cancellation) => Task.Run(() => {
        cancellation.ThrowIfCancellationRequested(); Directory.CreateDirectory(account.Profile);
        var bytes = Transform(Encoding.UTF8.GetBytes(value), account, true);
        var temporary = FileName(account) + ".writing-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, FileName(account), true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, cancellation);
    internal static Task Delete(Identity account, CancellationToken cancellation) => Task.Run(() => {
        cancellation.ThrowIfCancellationRequested();
        try { File.Delete(FileName(account)); } catch (DirectoryNotFoundException) { }
    }, cancellation);
}
