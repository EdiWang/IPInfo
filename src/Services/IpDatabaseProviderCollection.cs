namespace IPInfo.Services;

internal sealed class IpDatabaseProviderCollection(IReadOnlyList<IIpLocationProvider> providers)
{
    internal IReadOnlyList<IIpLocationProvider> Providers { get; } = providers;

    public int Count => Providers.Count;

    public bool HasAnyAvailable => Providers.Any(provider => provider.IsAvailable);

    public bool AreAllAvailable => Providers.Count > 0 && Providers.All(provider => provider.IsAvailable);

    public IReadOnlyList<DbFileInfo> GetFileInfos()
    {
        return Providers.Select(provider => provider.GetFileInfo()).ToArray();
    }

    internal void TryReloadAll(ILogger logger)
    {
        for (var i = 0; i < Providers.Count; i++)
        {
            try
            {
                Providers[i].TryReload();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reload configured IP database at index {Index}.", i);
            }
        }
    }
}
