using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UseScope.Desktop;

// Security.framework keeps secrets out of argv, shell scripts and JSON settings.
internal static class SecretVault
{
    private const string Cf = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Sec = "/System/Library/Frameworks/Security.framework/Security";
    private static readonly IntPtr Security = NativeLibrary.Load(Sec);
    private static readonly IntPtr Foundation = NativeLibrary.Load(Cf);
    private static IntPtr Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(name == "kCFBooleanTrue" ? Foundation : Security, name));
    [DllImport(Cf)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);
    [DllImport(Cf)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint count);
    [DllImport(Cf)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(Cf)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(Cf)] private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(Cf)] private static extern void CFRelease(IntPtr value);
    [DllImport(Sec)] private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [DllImport(Sec)] private static extern int SecItemAdd(IntPtr query, IntPtr result);
    [DllImport(Sec)] private static extern int SecItemUpdate(IntPtr query, IntPtr attributes);
    [DllImport(Sec)] private static extern int SecItemDelete(IntPtr query);
    private static T WithQuery<T>(Identity account, Func<Dictionary<string, IntPtr>, List<IntPtr>, T> action)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var allocated = new List<IntPtr>();
        IntPtr Text(string value) { var pointer = CFStringCreateWithCString(IntPtr.Zero, value, 0x08000100); allocated.Add(pointer); return pointer; }
        var values = new Dictionary<string, IntPtr> { ["kSecClass"] = Constant("kSecClassGenericPassword"),
            ["kSecAttrService"] = Text("io.github.zokahase-design.usescope.api"), ["kSecAttrAccount"] = Text(account.Vendor + ":" + account.Key) };
        try { return action(values, allocated); } finally { foreach (var pointer in allocated) if (pointer != IntPtr.Zero) CFRelease(pointer); }
    }
    private static IntPtr Dictionary(Dictionary<string, IntPtr> values) => CFDictionaryCreate(IntPtr.Zero,
        values.Keys.Select(Constant).ToArray(), values.Values.ToArray(), values.Count,
        NativeLibrary.GetExport(Foundation, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(Foundation, "kCFTypeDictionaryValueCallBacks"));
    internal static Task<string?> Read(Identity account, CancellationToken cancellation) => Task.Run(() => WithQuery<string?>(account, (values, _) => {
        cancellation.ThrowIfCancellationRequested(); values["kSecReturnData"] = Constant("kCFBooleanTrue");
        values["kSecUseAuthenticationUI"] = Constant("kSecUseAuthenticationUIFail");
        values["kSecMatchLimit"] = Constant("kSecMatchLimitOne"); var query = Dictionary(values);
        try {
            var status = SecItemCopyMatching(query, out var result); if (status == -25300) return null;
            if (status == -25308) throw new RemoteFailure("Schlüsselbund gesperrt · API-Schlüssel erneut hinterlegen");
            if (status != 0) throw new IOException("Schlüsselbundzugriff fehlgeschlagen (" + status + ").");
            try { var bytes = new byte[checked((int)CFDataGetLength(result))]; Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, bytes.Length);
                try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
            } finally { CFRelease(result); }
        } finally { CFRelease(query); }
    }), cancellation);
    internal static Task Write(Identity account, string value, CancellationToken cancellation) => Task.Run(() => WithQuery(account, (values, allocated) => {
        cancellation.ThrowIfCancellationRequested(); var bytes = Encoding.UTF8.GetBytes(value);
        IntPtr data; try { data = CFDataCreate(IntPtr.Zero, bytes, bytes.Length); } finally { CryptographicOperations.ZeroMemory(bytes); }
        allocated.Add(data); var query = Dictionary(values);
        try {
            var attributes = Dictionary(new() { ["kSecValueData"] = data }); int status;
            try { status = SecItemUpdate(query, attributes); } finally { CFRelease(attributes); }
            if (status == -25300) { values["kSecValueData"] = data; var add = Dictionary(values); try { status = SecItemAdd(add, IntPtr.Zero); } finally { CFRelease(add); } }
            if (status != 0) throw new IOException("API-Schlüssel konnte nicht im Schlüsselbund gespeichert werden (" + status + ").");
            return true;
        } finally { CFRelease(query); }
    }), cancellation);
    internal static Task Delete(Identity account, CancellationToken cancellation) => Task.Run(() => WithQuery(account, (values, _) => {
        cancellation.ThrowIfCancellationRequested(); var query = Dictionary(values);
        try { var status = SecItemDelete(query); if (status is not (0 or -25300)) throw new IOException("Schlüsselbundzugriff fehlgeschlagen (" + status + ")."); return true; }
        finally { CFRelease(query); }
    }), cancellation);
}
