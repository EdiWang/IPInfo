using System.Net;

namespace IPInfo.Services;

public static class ClientIpResolver
{
    public static ClientIpResolution? ResolveClientIp(HttpContext context)
    {
        var xff = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xff))
        {
            var leftmost = xff.Split(',', StringSplitOptions.TrimEntries)[0];
            if (TryParseIpAddress(leftmost, out var parsed))
            {
                return ClientIpResolution.From(parsed);
            }
        }

        var remote = context.Connection.RemoteIpAddress;
        return remote is null ? null : ClientIpResolution.From(remote);
    }

    public static IPAddress? ResolveClientIpV4(HttpContext context)
    {
        return ResolveClientIp(context)?.IpV4;
    }

    private static bool TryParseIpAddress(string value, out IPAddress ipAddress)
    {
        if (!IPAddress.TryParse(value, out var parsed))
        {
            ipAddress = IPAddress.None;
            return false;
        }

        ipAddress = parsed;
        return true;
    }
}
