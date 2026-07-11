using IPInfo.Models;
using IPInfo.Services;
using System.Net;
using System.Net.Sockets;

namespace IPInfo.Endpoints;

public static class IpInfoEndpoints
{
    public static void MapIpInfoEndpoints(this WebApplication app)
    {
        var ipGroup = app.MapGroup("/")
            .RequireRateLimiting("global")
            .RequireRateLimiting("per-ip");

        ipGroup.Map("/", HandleSelfLookup);
        ipGroup.Map("/ip", HandleSelfLookup);
        ipGroup.MapGet("/ip/{ipV4}", HandleSpecificLookup);

        app.MapGet("/db-info", (QqwryDbProvider db) =>
        {
            var info = db.GetFileInfo();
            return Results.Ok(new
            {
                fileName = Path.GetFileName(info.Path),
                sizeMb = Math.Round(info.SizeBytes / 1024.0 / 1024.0, 2),
                lastUpdatedUtc = info.LastUpdatedUtc
            });
        });
    }

    private static IResult HandleSelfLookup(HttpContext ctx, IpLookupService svc, ILogger<Program> logger)
    {
        var clientIpInfo = ClientIpResolver.ResolveClientIp(ctx);
        if (clientIpInfo is null)
        {
            logger.LogInformation("Lookup {ClientIp} -> self: unable to resolve client IP", "N/A");
            return Results.Problem(
                detail: "Unable to resolve client IP address.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (clientIpInfo.IpV4 is null)
        {
            logger.LogInformation("Lookup {ClientIp} -> self: IPv6 detected without IPv4", clientIpInfo.Address);
            return Results.Ok(new IpLocationResult
            {
                QueryIp = clientIpInfo.Address.ToString(),
                ClientIpV4 = null,
                ClientIpV6 = clientIpInfo.IpV6?.ToString(),
                Country = string.Empty,
                Area = string.Empty,
                Isp = string.Empty
            });
        }

        var result = svc.Lookup(clientIpInfo.IpV4, clientIpInfo);
        var ua = ctx.Request.Headers.UserAgent.ToString();
        logger.LogInformation("Lookup {ClientIp} -> {QueryIp}: {Country} {Area} {Isp} | UA: {UserAgent}",
            clientIpInfo.Address, result.QueryIp, result.Country, result.Area, result.Isp, ua);
        return Results.Ok(result);
    }

    private static IResult HandleSpecificLookup(
        string ipV4,
        HttpContext ctx,
        IpLookupService svc,
        ILogger<Program> logger)
    {
        var clientIp = ClientIpResolver.ResolveClientIp(ctx);
        var clientIpLogValue = clientIp?.Address.ToString() ?? "unknown";

        if (!IPAddress.TryParse(ipV4, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        {
            logger.LogInformation("Lookup {ClientIp} -> {QueryIp}: invalid IPv4", clientIpLogValue, ipV4);
            return Results.Problem(
                detail: $"'{ipV4}' is not a valid IPv4 address.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = svc.Lookup(ip, clientIp);
        var ua = ctx.Request.Headers.UserAgent.ToString();
        logger.LogInformation("Lookup {ClientIp} -> {QueryIp}: {Country} {Area} {Isp} | UA: {UserAgent}",
            clientIpLogValue, result.QueryIp, result.Country, result.Area, result.Isp, ua);
        return Results.Ok(result);
    }
}
