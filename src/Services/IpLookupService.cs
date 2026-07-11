using IPInfo.Models;
using System.Net;

namespace IPInfo.Services;

public sealed class IpLookupService(QqwryDbProvider db)
{
    public IpLocationResult Lookup(IPAddress ip, ClientIpResolution? clientIp = null)
    {
        var location = db.Query(ip);
        return new IpLocationResult
        {
            QueryIp = ip.ToString(),
            ClientIpV4 = clientIp?.IpV4?.ToString(),
            ClientIpV6 = clientIp?.IpV6?.ToString(),
            Country = location.Country,
            Area = string.Empty,
            Isp = location.Area
        };
    }
}
