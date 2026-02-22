namespace IPInfo.Models;

public sealed class IpLocationResult
{
    public string QueryIp { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string Isp { get; init; } = string.Empty;
}
