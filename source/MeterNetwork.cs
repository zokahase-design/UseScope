using System.Net;
using System.Net.Sockets;

namespace UseScope.Desktop;

internal static class MeterNetwork
{
    internal static Uri ValidateBase(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Bitte die HTTPS-API-Basisadresse ohne Schlüssel, Query oder Benutzername angeben.");
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(uri.Host, out var ip) && !Public(ip)))
            throw new ArgumentException("Nur öffentliche HTTPS-Anbieteradressen sind zugelassen.");
        return uri;
    }
    internal static bool Public(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return (bytes[0] & 0xe0) == 0x20 && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
        return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
            && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            && !(bytes[0] == 169 && bytes[1] == 254) && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            && !(bytes[0] == 192 && bytes[1] is 0 or 168) && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            && !(bytes[0] == 198 && bytes[1] is 18 or 19) && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }
    internal static SocketsHttpHandler Handler() => new() {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All, MaxConnectionsPerServer = 8, PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellation) => {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellation);
            if (addresses.Length == 0 || addresses.Any(address => !Public(address))) throw new HttpRequestException("Nichtöffentliche API-Adresse blockiert.");
            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellation); return new NetworkStream(socket, true); }
                catch (Exception error) when (error is SocketException or IOException) { socket.Dispose(); last = error; }
                catch { socket.Dispose(); throw; }
            }
            throw new HttpRequestException("Anbieter nicht erreichbar.", last);
        }
    };
    internal static bool InferencePath(string method, string path)
    {
        if (method == "GET") return path == "/models";
        if (method != "POST") return false;
        return new[] { "/chat/completions", "/responses", "/completions", "/embeddings", "/messages", "/chat", "/embed", "/images/generations", "/images/edits", "/images/variations", "/audio/speech", "/audio/transcriptions", "/audio/translations", "/rerank", "/reranking" }.Contains(path, StringComparer.Ordinal)
            || System.Text.RegularExpressions.Regex.IsMatch(path, "^/models/[A-Za-z0-9_.-]+:(generateContent|streamGenerateContent)$");
    }
    internal static string RelativeRoute(string path)
    {
        // API paths need no percent escapes. Reject encoded separators/dot segments before any URI normalization.
        if (path.Length is 0 or > 2048 || path.Contains('%') || path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Any(char.IsControl)
            || path.Split('/').Any(segment => segment is "." or ".." || segment.Length == 0)) throw new ArgumentException("Ungültiger API-Pfad.");
        return path;
    }
}
