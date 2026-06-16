using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IPInfo.Services;

public sealed class QqwryDbHealthCheck(QqwryDbProvider db) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = db.IsAvailable
            ? HealthCheckResult.Healthy("QQWry database is available.")
            : HealthCheckResult.Unhealthy("QQWry database is unavailable.");

        return Task.FromResult(result);
    }
}
