using IPInfo.Endpoints;
using IPInfo.Middleware;
using IPInfo.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var version = typeof(Program).Assembly
    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "unknown";
Console.WriteLine($"IPInfo v{version}");

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

// ── IP Databases ─────────────────────────────────────────────────
var ipDatabaseProviders = IpDatabaseOptions.GetProviders(builder.Configuration);

builder.Services.AddSingleton<IReadOnlyList<IIpLocationProvider>>(sp =>
{
    return ipDatabaseProviders.Select<IpDatabaseProviderOptions, IIpLocationProvider>(provider =>
    {
        if (provider.Type.Equals(IpDatabaseProviderTypes.Qqwry, StringComparison.OrdinalIgnoreCase))
        {
            return new QqwryDbProvider(
                provider.Path,
                sp.GetRequiredService<ILogger<QqwryDbProvider>>());
        }

        if (provider.Type.Equals(IpDatabaseProviderTypes.MaxMindGeoLite2City, StringComparison.OrdinalIgnoreCase))
        {
            return new MaxMindGeoLite2CityProvider(
                provider.Path,
                provider.Locale,
                sp.GetRequiredService<ILogger<MaxMindGeoLite2CityProvider>>());
        }

        throw new InvalidOperationException($"Unsupported IP database provider type '{provider.Type}'.");
    }).ToArray();
});
builder.Services.AddSingleton<IpDatabaseProviderCollection>();
builder.Services.AddHostedService<IpDatabaseWatcher>();
builder.Services.AddSingleton<IpLookupService>();
builder.Services.AddSingleton<DbAvailabilityLogState>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<IpDatabaseHealthCheck>("ip-databases", tags: ["ready"]);

// ── Forwarded Headers ────────────────────────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Clear default constraints so it works behind any Docker / K8s proxy
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Rate Limiting ────────────────────────────────────────────────
var perIpPerSecond = builder.Configuration.GetValue("RateLimiting:PerIpPerSecond", 5);
var globalPerSecond = builder.Configuration.GetValue("RateLimiting:GlobalPerSecond", 10);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await ProblemDetailsResponse.WriteAsync(
            context.HttpContext,
            StatusCodes.Status429TooManyRequests,
            "https://tools.ietf.org/html/rfc6585#section-4",
            "Too Many Requests",
            "Rate limit exceeded. Please try again later.",
            cancellationToken);
    };

    // Global fixed-window limiter
    options.AddFixedWindowLimiter("global", opt =>
    {
        opt.PermitLimit = globalPerSecond;
        opt.Window = TimeSpan.FromSeconds(1);
        opt.QueueLimit = 0;
    });

    // Per-IP fixed-window limiter
    options.AddPolicy("per-ip", context =>
    {
        var ip = ClientIpResolver.ResolveClientIp(context)?.Address.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perIpPerSecond,
            Window = TimeSpan.FromSeconds(1),
            QueueLimit = 0
        });
    });
});

// ── Problem Details ──────────────────────────────────────────────
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseIpDatabaseAvailabilityGate();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.MapIpInfoEndpoints();

app.Run();

public partial class Program;
