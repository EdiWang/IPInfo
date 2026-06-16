using System.Net;
using System.Net.Sockets;

namespace IPInfo.Services;

public static class ClientIpResolver
{
    public static IPAddress? ResolveClientIpV4(HttpContext context)
    {
        var xff = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xff))
        {
            var leftmost = xff.Split(',', StringSplitOptions.TrimEntries)[0];
            if (IPAddress.TryParse(leftmost, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                return parsed;
            }
        }

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null) return null;

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return remote.AddressFamily == AddressFamily.InterNetwork ? remote : null;
    }
}
