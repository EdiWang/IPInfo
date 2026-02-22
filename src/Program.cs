using IPInfo.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var version = typeof(Program).Assembly
    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "unknown";
Console.WriteLine($"IPInfo v{version}");

var builder = WebApplication.CreateBuilder(args);

// ── QQWry.dat ────────────────────────────────────────────────────
var qqwryPath = builder.Configuration.GetValue<string>("DBPath") ?? "/data/qqwry.dat";

builder.Services.AddSingleton(sp =>
    new QqwryDbProvider(qqwryPath, sp.GetRequiredService<ILogger<QqwryDbProvider>>()));
builder.Services.AddHostedService<QqwryDbWatcher>();
builder.Services.AddSingleton<IpLookupService>();

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
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = 429,
            detail = "Rate limit exceeded. Please try again later."
        }, cancellationToken);
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
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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

// ── DB availability gate ─────────────────────────────────────────
app.Use(async (ctx, next) =>
{
    var db = ctx.RequestServices.GetRequiredService<QqwryDbProvider>();
    if (!db.IsAvailable)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError("IP database not found at '{Path}'. Returning 503. Please check the configuration.", qqwryPath);
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            title = "IP Database Unavailable",
            status = 503,
            detail = $"IP database not found at the configured path '{qqwryPath}'. Please check the configuration and ensure the database file exists."
        });
        return;
    }
    await next(ctx);
});

// ── Helper: resolve client IPv4 from X-Forwarded-For (leftmost) ─
static IPAddress? ResolveClientIpV4(HttpContext ctx)
{
    // Try X-Forwarded-For header first (leftmost = original client)
    var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrEmpty(xff))
    {
        var leftmost = xff.Split(',', StringSplitOptions.TrimEntries)[0];
        if (IPAddress.TryParse(leftmost, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
            return parsed;
    }

    var remote = ctx.Connection.RemoteIpAddress;
    if (remote is null) return null;

    // Handle IPv4-mapped IPv6 addresses (::ffff:x.x.x.x)
    if (remote.IsIPv4MappedToIPv6)
        remote = remote.MapToIPv4();

    return remote.AddressFamily == AddressFamily.InterNetwork ? remote : null;
}

// ── Endpoints ────────────────────────────────────────────────────
var ipGroup = app.MapGroup("/")
    .RequireRateLimiting("global")
    .RequireRateLimiting("per-ip");

// GET / or GET /ip — lookup caller's own IP
ipGroup.Map("/", HandleSelfLookup);
ipGroup.Map("/ip", HandleSelfLookup);

static IResult HandleSelfLookup(HttpContext ctx, IpLookupService svc, ILogger<Program> logger)
{
    var clientIp = ResolveClientIpV4(ctx);
    if (clientIp is null)
    {
        logger.LogInformation("Lookup {ClientIp} -> self: unable to resolve IPv4", "N/A");
        return Results.Problem(
            detail: "Unable to resolve client IPv4 address.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var result = svc.Lookup(clientIp);
    logger.LogInformation("Lookup {ClientIp} -> {QueryIp}: {Country} {Area} {Isp}",
        clientIp, result.QueryIp, result.Country, result.Area, result.Isp);
    return Results.Ok(result);
}

// GET /ip/{ipV4} — lookup a specific IP
ipGroup.MapGet("/ip/{ipV4}", (string ipV4, HttpContext ctx, IpLookupService svc, ILogger<Program> logger) =>
{
    var clientIp = ResolveClientIpV4(ctx)?.ToString() ?? "unknown";

    if (!IPAddress.TryParse(ipV4, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
    {
        logger.LogInformation("Lookup {ClientIp} -> {QueryIp}: invalid IPv4", clientIp, ipV4);
        return Results.Problem(
            detail: $"'{ipV4}' is not a valid IPv4 address.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var result = svc.Lookup(ip);
    logger.LogInformation("Lookup {ClientIp} -> {QueryIp}: {Country} {Area} {Isp}",
        clientIp, result.QueryIp, result.Country, result.Area, result.Isp);
    return Results.Ok(result);
});

// GET /db-info — return database file metadata
app.MapGet("/db-info", (QqwryDbProvider db) =>
{
    var info = db.GetFileInfo();
    return Results.Ok(new
    {
        path = info.Path,
        sizeMb = Math.Round(info.SizeBytes / 1024.0 / 1024.0, 2),
        lastUpdatedUtc = info.LastUpdatedUtc
    });
});

app.Run();
