namespace IPInfo.Services;

internal sealed class IpDatabaseWatcher(
    IpDatabaseProviderCollection databases,
    IConfiguration configuration,
    ILogger<IpDatabaseWatcher> logger) : BackgroundService
{
    private readonly TimeSpan _interval = IpDbReloadOptions.GetReloadInterval(configuration, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                databases.TryReloadAll(logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reload configured IP databases.");
            }
        }
    }
}
