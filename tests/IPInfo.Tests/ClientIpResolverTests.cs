using IPInfo.Services;
using Microsoft.AspNetCore.Http;
using System.Net;
using Xunit;

namespace IPInfo.Tests;

public sealed class ClientIpResolverTests
{
    [Fact]
    public void ResolveClientIp_UsesLeftmostXForwardedForAddress()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "2001:db8::8, 10.0.0.1";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.10");

        var result = ClientIpResolver.ResolveClientIp(context);

        Assert.NotNull(result);
        Assert.Equal(IPAddress.Parse("2001:db8::8"), result.Address);
        Assert.Null(result.IpV4);
        Assert.Equal(IPAddress.Parse("2001:db8::8"), result.IpV6);
    }

    [Fact]
    public void ResolveClientIpV4_UsesLeftmostXForwardedForAddress()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "1.1.1.8, 10.0.0.1";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.10");

        var result = ClientIpResolver.ResolveClientIpV4(context);

        Assert.Equal(IPAddress.Parse("1.1.1.8"), result);
    }

    [Fact]
    public void ResolveClientIpV4_FallsBackToRemoteIp_WhenXForwardedForIsInvalid()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "not-an-ip";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.0.10");

        var result = ClientIpResolver.ResolveClientIpV4(context);

        Assert.Equal(IPAddress.Parse("192.168.0.10"), result);
    }

    [Fact]
    public void ResolveClientIpV4_MapsIpv4MappedIpv6RemoteIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.168.0.10");

        var result = ClientIpResolver.ResolveClientIpV4(context);

        Assert.Equal(IPAddress.Parse("192.168.0.10"), result);
    }

    [Fact]
    public void ResolveClientIpV4_MapsIpv4MappedIpv6XForwardedForAddress()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-For"] = "::ffff:192.168.0.10";

        var result = ClientIpResolver.ResolveClientIpV4(context);

        Assert.Equal(IPAddress.Parse("192.168.0.10"), result);
    }

    [Fact]
    public void ResolveClientIpV4_ReturnsNull_WhenNoIpv4IsAvailable()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");

        var result = ClientIpResolver.ResolveClientIpV4(context);

        Assert.Null(result);
    }
}
