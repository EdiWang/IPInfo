namespace IPInfo.Models;

public sealed class IpLocationResult
{
    public string QueryIp { get; init; } = string.Empty;
    public string? ClientIpV4 { get; init; }
    public string? ClientIpV6 { get; init; }
    public string Country { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public string Isp { get; init; } = string.Empty;
}
