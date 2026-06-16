namespace IPInfo.Services;

public static class IpDbReloadOptions
{
    public const int DefaultReloadIntervalSeconds = 60;
    public const int MinimumReloadIntervalSeconds = 1;

    public static TimeSpan GetReloadInterval(IConfiguration configuration, ILogger logger)
    {
        var configuredSeconds = configuration.GetValue("IpDb:ReloadIntervalSeconds", DefaultReloadIntervalSeconds);
        if (configuredSeconds >= MinimumReloadIntervalSeconds)
        {
            return TimeSpan.FromSeconds(configuredSeconds);
        }

        logger.LogWarning(
            "Invalid IpDb:ReloadIntervalSeconds value {ReloadIntervalSeconds}; using default {DefaultReloadIntervalSeconds}.",
            configuredSeconds,
            DefaultReloadIntervalSeconds);

        return TimeSpan.FromSeconds(DefaultReloadIntervalSeconds);
    }
}
