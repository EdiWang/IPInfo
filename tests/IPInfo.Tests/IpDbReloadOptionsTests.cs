using IPInfo.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IPInfo.Tests;

public sealed class IpDbReloadOptionsTests
{
    [Fact]
    public void GetReloadInterval_ReturnsConfiguredValue_WhenValueIsPositive()
    {
        var configuration = CreateConfiguration("15");

        var result = IpDbReloadOptions.GetReloadInterval(configuration, NullLogger.Instance);

        Assert.Equal(TimeSpan.FromSeconds(15), result);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void GetReloadInterval_ReturnsDefaultValue_WhenValueIsNotPositive(string configuredValue)
    {
        var configuration = CreateConfiguration(configuredValue);

        var result = IpDbReloadOptions.GetReloadInterval(configuration, NullLogger.Instance);

        Assert.Equal(TimeSpan.FromSeconds(IpDbReloadOptions.DefaultReloadIntervalSeconds), result);
    }

    private static IConfiguration CreateConfiguration(string reloadIntervalSeconds)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IpDb:ReloadIntervalSeconds"] = reloadIntervalSeconds
            })
            .Build();
    }
}
