using IPInfo.Services;

namespace IPInfo.Middleware;

public static class QqwryDbAvailabilityMiddlewareExtensions
{
    public static IApplicationBuilder UseQqwryDbAvailabilityGate(this IApplicationBuilder app, string qqwryPath)
    {
        return app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/health"))
            {
                await next(ctx);
                return;
            }

            var db = ctx.RequestServices.GetRequiredService<QqwryDbProvider>();
            if (!db.IsAvailable)
            {
                var logState = ctx.RequestServices.GetRequiredService<DbAvailabilityLogState>();
                if (logState.TryMarkUnavailable())
                {
                    var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
                    logger.LogError(
                        "IP database not found or not accessible at '{Path}'. Returning 503. Please check the configuration and file permissions.",
                        qqwryPath);
                }

                await ProblemDetailsResponse.WriteAsync(
                    ctx,
                    StatusCodes.Status503ServiceUnavailable,
                    "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                    "IP Database Unavailable",
                    $"IP database not found or not accessible at the configured path '{qqwryPath}'. Please check the configuration, file permissions, and ensure the database file exists.");
                return;
            }

            ctx.RequestServices.GetRequiredService<DbAvailabilityLogState>().MarkAvailable();
            await next(ctx);
        });
    }
}
