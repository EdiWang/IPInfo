using IPInfo.Services;

namespace IPInfo.Middleware;

public static class IpDatabaseAvailabilityMiddlewareExtensions
{
    public static IApplicationBuilder UseIpDatabaseAvailabilityGate(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/health"))
            {
                await next(ctx);
                return;
            }

            var databases = ctx.RequestServices.GetRequiredService<IpDatabaseProviderCollection>();
            if (!databases.HasAnyAvailable)
            {
                var logState = ctx.RequestServices.GetRequiredService<DbAvailabilityLogState>();
                if (logState.TryMarkUnavailable())
                {
                    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError(
                        "No configured IP database is available. Returning 503. Please check the configuration, file permissions, and update logs.");
                }

                await ProblemDetailsResponse.WriteAsync(
                    ctx,
                    StatusCodes.Status503ServiceUnavailable,
                    "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                    "IP Database Unavailable",
                    "No configured IP database is available. Please check the configuration, file permissions, and ensure at least one database file exists.");
                return;
            }

            ctx.RequestServices.GetRequiredService<DbAvailabilityLogState>().MarkAvailable();
            await next(ctx);
        });
    }
}
