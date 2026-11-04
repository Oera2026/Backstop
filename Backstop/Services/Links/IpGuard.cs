using System.Net;
using System.Net.Sockets;

namespace Backstop.Services.Links;

/// <summary>
/// Decides whether the Link check may talk to an address. Only ordinary public internet addresses pass.
/// Private, loopback, link-local (this includes the cloud metadata address 169.254.169.254), carrier-grade NAT,
/// reserved, documentation, multicast and unique-local ranges are all refused, for IPv4 and IPv6 alike.
/// </summary>
public static class IpGuard
{
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] == 0                                   // 0.0.0.0/8
                || b[0] == 10                                    // 10/8
                || b[0] == 127                                   // loopback
                || (b[0] == 100 && b[1] is >= 64 and <= 127)     // 100.64/10 carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                  // link-local and metadata
                || (b[0] == 172 && b[1] is >= 16 and <= 31)      // 172.16/12
                || (b[0] == 192 && b[1] == 168)                  // 192.168/16
                || (b[0] == 192 && b[1] == 88 && b[2] == 99)     // 6to4 relay anycast
                || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)  // 192.0.0/24, 192.0.2/24
                || (b[0] == 198 && b[1] is 18 or 19)             // benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // documentation
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)     // documentation
                || b[0] >= 224);                                 // multicast, reserved, broadcast
        }

        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return false;

        // NAT64 (64:ff9b::/96) and 6to4 (2002::/16) carry an IPv4 address inside: judge that one.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4..12].All(x => x == 0))
            return IsPublic(new IPAddress(b[12..16]));
        if (b[0] == 0x20 && b[1] == 0x02)
            return IsPublic(new IPAddress(b[2..6]));
        // Teredo (2001::/32) and documentation (2001:db8::/32).
        if (b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0 && b[3] == 0) || (b[2] == 0x0d && b[3] == 0xb8))) return false;

        // Only global unicast, 2000::/3. That leaves out ::1, ::, fc00::/7, fe80::/10, fec0::/10 and ff00::/8 in one rule.
        return (b[0] & 0xE0) == 0x20;
    }

    private static readonly string[] InternalSuffixes = [".localhost", ".local", ".internal", ".localdomain", ".lan", ".home.arpa", ".intranet", ".corp"];

    /// <summary>Why this address may not be opened, or null when it may. Looks at the address text only; the IP check happens after DNS.</summary>
    public static string? RefuseUrl(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return "Only web addresses (http or https) are opened.";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "The address has a name in front of it.";
        if (uri.Port is not (80 or 443)) return "The address uses an unusual port.";
        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return "The address has no website name.";
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
            return IsPublic(literal) ? null : "The address points to a private or internal place.";
        if (host == "localhost" || host == "metadata" || host == "metadata.google.internal" || InternalSuffixes.Any(s => host.EndsWith(s)))
            return "The address points to a private or internal place.";
        if (!host.Contains('.')) return "The address points to a private or internal place.";
        return null;
    }

    /// <summary>Addresses that exist only for examples and tests (RFC 2606). They never reach a real site, so they are not opened.</summary>
    public static bool IsReservedExample(string host)
    {
        host = host.TrimEnd('.').ToLowerInvariant();
        string[] names = ["example.com", "example.net", "example.org"];
        return names.Any(n => host == n || host.EndsWith("." + n))
            || host.EndsWith(".test") || host.EndsWith(".example") || host.EndsWith(".invalid");
    }
}
