using IPInfo.Models;
using System.Net;

namespace IPInfo.Services;

public sealed class IpLookupService(QqwryDbProvider db)
{
    public IpLocationResult Lookup(IPAddress ip)
    {
        var location = db.Query(ip);
        return new IpLocationResult
        {
            QueryIp = ip.ToString(),
            Country = location.Country,
            Area = string.Empty,
            Isp = location.Area
        };
    }
}
