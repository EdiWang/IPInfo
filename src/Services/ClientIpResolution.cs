using System.Net;
using System.Net.Sockets;

namespace IPInfo.Services;

public sealed record ClientIpResolution(IPAddress Address, IPAddress? IpV4, IPAddress? IpV6)
{
    public static ClientIpResolution From(IPAddress address)
    {
        var normalized = Normalize(address);
        return normalized.AddressFamily switch
        {
            AddressFamily.InterNetwork => new ClientIpResolution(normalized, normalized, null),
            AddressFamily.InterNetworkV6 => new ClientIpResolution(normalized, null, normalized),
            _ => new ClientIpResolution(normalized, null, null)
        };
    }

    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
