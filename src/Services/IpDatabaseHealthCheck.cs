using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IPInfo.Services;

internal sealed class IpDatabaseHealthCheck(IpDatabaseProviderCollection databases) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = databases.AreAllAvailable
            ? HealthCheckResult.Healthy("All configured IP databases are available.")
            : HealthCheckResult.Unhealthy("One or more configured IP databases are unavailable.");

        return Task.FromResult(result);
    }
}
