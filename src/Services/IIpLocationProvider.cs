using System.Net;

namespace IPInfo.Services;

internal interface IIpLocationProvider
{
    bool IsAvailable { get; }

    ProviderIpLocation Lookup(IPAddress ip);

    DbFileInfo GetFileInfo();

    void TryReload();
}

internal sealed record ProviderIpLocation(string Country, string Area, string Isp)
{
    public static ProviderIpLocation Empty { get; } = new(string.Empty, string.Empty, string.Empty);
}
