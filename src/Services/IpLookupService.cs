using IPInfo.Models;
using System.Net;

namespace IPInfo.Services;

internal sealed class IpLookupService(
    IpDatabaseProviderCollection databases,
    ILogger<IpLookupService> logger)
{
    public IpLocationResult Lookup(IPAddress ip, ClientIpResolution? clientIp = null)
    {
        var locations = LookupAll(ip);
        return new IpLocationResult
        {
            QueryIp = ip.ToString(),
            ClientIpV4 = clientIp?.IpV4?.ToString(),
            ClientIpV6 = clientIp?.IpV6?.ToString(),
            Country = locations.Select(location => location.Country).ToArray(),
            Area = locations.Select(location => location.Area).ToArray(),
            Isp = locations.Select(location => location.Isp).ToArray()
        };
    }

    public IpLocationResult CreateEmptyResult(string queryIp, ClientIpResolution? clientIp = null)
    {
        return new IpLocationResult
        {
            QueryIp = queryIp,
            ClientIpV4 = clientIp?.IpV4?.ToString(),
            ClientIpV6 = clientIp?.IpV6?.ToString(),
            Country = CreateEmptyValues(),
            Area = CreateEmptyValues(),
            Isp = CreateEmptyValues()
        };
    }

    private ProviderIpLocation[] LookupAll(IPAddress ip)
    {
        var results = new ProviderIpLocation[databases.Count];
        for (var i = 0; i < databases.Providers.Count; i++)
        {
            var provider = databases.Providers[i];
            if (!provider.IsAvailable)
            {
                results[i] = ProviderIpLocation.Empty;
                continue;
            }

            try
            {
                results[i] = provider.Lookup(ip);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Configured IP database provider at index {Index} failed to resolve {Ip}.", i, ip);
                results[i] = ProviderIpLocation.Empty;
            }
        }

        return results;
    }

    private string[] CreateEmptyValues()
    {
        return Enumerable.Repeat(string.Empty, databases.Count).ToArray();
    }
}
