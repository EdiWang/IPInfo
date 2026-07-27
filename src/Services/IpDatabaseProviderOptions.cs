namespace IPInfo.Services;

public sealed class IpDatabaseProviderOptions
{
    public bool Enabled { get; init; } = true;
    public string Type { get; init; } = IpDatabaseProviderTypes.Qqwry;
    public string Path { get; init; } = string.Empty;
    public string Locale { get; init; } = "zh-CN";
}

public static class IpDatabaseProviderTypes
{
    public const string Qqwry = "Qqwry";
    public const string MaxMindGeoLite2City = "MaxMindGeoLite2City";
}

public static class IpDatabaseOptions
{
    public static IReadOnlyList<IpDatabaseProviderOptions> GetProviders(IConfiguration configuration)
    {
        var configuredProviders = configuration
            .GetSection("IpDatabases:Providers")
            .Get<IpDatabaseProviderOptions[]>();

        if (configuredProviders is { Length: > 0 })
        {
            return configuredProviders
                .Where(provider => provider.Enabled)
                .Select(Normalize)
                .ToArray();
        }

        var legacyQqwryPath = configuration.GetValue<string>("DBPath") ?? "/data/qqwry.dat";
        return
        [
            new IpDatabaseProviderOptions
            {
                Type = IpDatabaseProviderTypes.Qqwry,
                Path = legacyQqwryPath
            }
        ];
    }

    private static IpDatabaseProviderOptions Normalize(IpDatabaseProviderOptions provider)
    {
        var type = string.IsNullOrWhiteSpace(provider.Type)
            ? IpDatabaseProviderTypes.Qqwry
            : provider.Type.Trim();

        var path = string.IsNullOrWhiteSpace(provider.Path)
            ? GetDefaultPath(type)
            : provider.Path;

        return new IpDatabaseProviderOptions
        {
            Enabled = provider.Enabled,
            Type = type,
            Path = path,
            Locale = string.IsNullOrWhiteSpace(provider.Locale) ? "zh-CN" : provider.Locale
        };
    }

    private static string GetDefaultPath(string type)
    {
        return type.Equals(IpDatabaseProviderTypes.MaxMindGeoLite2City, StringComparison.OrdinalIgnoreCase)
            ? "/data/GeoLite2-City.mmdb"
            : "/data/qqwry.dat";
    }
}
