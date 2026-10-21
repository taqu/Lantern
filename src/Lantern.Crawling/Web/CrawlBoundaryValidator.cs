using System.Net;
using System.Net.Sockets;
using Lantern.Core.Crawling;

namespace Lantern.Crawling.Web;

/// <summary>
/// Enforces host, path, and SSRF boundaries for web crawling.
/// Intranet hosts are allowed when explicitly listed in AllowedHosts.
/// Loopback and link-local metadata addresses are always blocked.
/// </summary>
public sealed class CrawlBoundaryValidator
{
    // Always-blocked IP ranges regardless of AllowedHosts
    private static readonly (byte[] Network, int PrefixLength)[] BlockedRanges =
    [
        (new byte[] { 127, 0, 0, 0 }, 8),         // 127.0.0.0/8 loopback
        (new byte[] { 169, 254, 0, 0 }, 16),       // 169.254.0.0/16 link-local / metadata
        (new byte[] { 0, 0, 0, 0 }, 8),            // 0.0.0.0/8 this network
        (new byte[] { 100, 64, 0, 0 }, 10),        // 100.64.0.0/10 CGNAT / AWS metadata
        (new byte[] { 192, 0, 2, 0 }, 24),         // 192.0.2.0/24 TEST-NET-1
        (new byte[] { 198, 51, 100, 0 }, 24),      // 198.51.100.0/24 TEST-NET-2
        (new byte[] { 203, 0, 113, 0 }, 24),       // 203.0.113.0/24 TEST-NET-3
        (new byte[] { 240, 0, 0, 0 }, 4),          // 240.0.0.0/4 reserved
    ];

    public bool IsUrlInScope(string url, WebCrawlerConfiguration config)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u)) return false;
        if (u.Scheme != "http" && u.Scheme != "https") return false;
        if (!string.IsNullOrEmpty(u.UserInfo)) return false;

        string host = u.Host.ToLowerInvariant();
        if (!IsHostAllowed(host, config.AllowedHosts)) return false;
        if (!IsPathAllowed(u.AbsolutePath, config.AllowedPathPrefixes)) return false;
        if (IsPatternExcluded(url, config.ExcludeUrlPatterns)) return false;
        return true;
    }

    public static bool IsHostAllowed(string host, IReadOnlyList<string> allowedHosts) =>
        allowedHosts.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase));

    public static bool IsPathAllowed(string path, IReadOnlyList<string> prefixes)
    {
        if (prefixes.Count == 0) return true;
        foreach (string prefix in prefixes)
        {
            string norm = prefix.TrimEnd('/');
            if (norm.Length == 0) return true; // "/" prefix = allow all
            // Require exact prefix OR prefix followed by / to prevent /docs matching /docs-private
            if (path.Equals(norm, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(norm + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool IsPatternExcluded(string url, IReadOnlyList<string> patterns)
    {
        foreach (string pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            if (url.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Validates that all DNS-resolved addresses for a host are acceptable.
    /// Loopback and metadata addresses are always blocked.
    /// Private ranges are allowed when host is in AllowedHosts (intranet support).
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateHostAsync(
        string host, IReadOnlyList<string> allowedHosts, CancellationToken ct = default)
    {
        if (!IsHostAllowed(host, allowedHosts))
            return (false, $"Host '{host}' is not in the allowed hosts list.");

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException ex)
        {
            return (false, $"DNS resolution failed for '{host}': {ex.Message}");
        }

        if (addresses.Length == 0)
            return (false, $"DNS returned no addresses for '{host}'.");

        foreach (IPAddress ip in addresses)
        {
            // Loopback is always blocked (prevents server talking to itself)
            if (IPAddress.IsLoopback(ip))
                return (false, $"Destination '{host}' resolves to loopback address {ip}.");

            // Link-local metadata is always blocked (AWS 169.254.169.254, etc.)
            if (IsInBlockedRange(ip))
                return (false, $"Destination '{host}' resolves to a blocked address range ({ip}).");
        }

        return (true, null);
    }

    private static bool IsInBlockedRange(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // Block IPv6 loopback (::1) and link-local (fe80::/10)
            if (ip.Equals(IPAddress.IPv6Loopback)) return true;
            byte[] bytes = ip.GetAddressBytes();
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true; // fe80::/10
            if (bytes[0] == 0xfc || bytes[0] == 0xfd) return false; // fc00::/7 unique local — ALLOWED for intranet
            return false;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] addr = ip.GetAddressBytes();

        foreach ((byte[] network, int prefix) in BlockedRanges)
        {
            if (IsInCidr(addr, network, prefix)) return true;
        }
        return false;
    }

    private static bool IsInCidr(byte[] addr, byte[] network, int prefixLength)
    {
        for (int i = 0; i < 4; i++)
        {
            int bits = Math.Min(8, prefixLength - i * 8);
            if (bits <= 0) break;
            int mask = bits >= 8 ? 0xFF : (0xFF << (8 - bits)) & 0xFF;
            if ((addr[i] & mask) != (network[i] & mask)) return false;
        }
        return true;
    }
}
