namespace IPInfo.Services;

public sealed class QqwryDbWatcher(
    QqwryDbProvider provider,
    IConfiguration configuration,
    ILogger<QqwryDbWatcher> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(configuration.GetValue("IpDb:ReloadIntervalSeconds", 60));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                provider.TryReload();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reload QQWry database.");
            }
        }
    }
}
