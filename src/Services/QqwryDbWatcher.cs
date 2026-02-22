namespace IPInfo.Services;

public sealed class QqwryDbWatcher : BackgroundService
{
    private readonly QqwryDbProvider _provider;
    private readonly TimeSpan _interval;
    private readonly ILogger<QqwryDbWatcher> _logger;

    public QqwryDbWatcher(QqwryDbProvider provider, IConfiguration configuration, ILogger<QqwryDbWatcher> logger)
    {
        _provider = provider;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(configuration.GetValue("IpDb:ReloadIntervalSeconds", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _provider.TryReload();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload QQWry database.");
            }
        }
    }
}
