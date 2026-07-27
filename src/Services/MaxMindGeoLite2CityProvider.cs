using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using System.Net;

namespace IPInfo.Services;

internal sealed class MaxMindGeoLite2CityProvider : IIpLocationProvider
{
    private enum DbFileState
    {
        Exists,
        Missing,
        Unreadable
    }

    private const long MinValidFileSizeBytes = 100 * 1024;
    private const int MissingFileReloadsBeforeUnavailable = 3;

    private volatile DatabaseReader? _current;
    private readonly string _path;
    private readonly string _locale;
    private readonly ILogger<MaxMindGeoLite2CityProvider> _logger;
    private DateTime _lastWriteTime;
    private int _missingReloadCount;

    public bool IsAvailable => _current is not null;

    public MaxMindGeoLite2CityProvider(
        string path,
        string locale,
        ILogger<MaxMindGeoLite2CityProvider> logger)
    {
        _path = path;
        _locale = locale;
        _logger = logger;
        TryLoad(initialLoad: true);
    }

    public ProviderIpLocation Lookup(IPAddress ip)
    {
        var reader = _current ?? throw new InvalidOperationException(
            $"MaxMind GeoLite2 City database is not available at the configured path '{_path}'.");

        try
        {
            var response = reader.City(ip);
            var country = ReadLocalizedName(response.Country.Names, response.Country.Name, response.Country.IsoCode);
            var mostSpecificSubdivision = response.Subdivisions.LastOrDefault();
            var subdivision = ReadLocalizedName(
                mostSpecificSubdivision?.Names,
                mostSpecificSubdivision?.Name,
                mostSpecificSubdivision?.IsoCode);
            var city = ReadLocalizedName(response.City.Names, response.City.Name, null);
            var area = string.Join(" ", new[] { subdivision, city }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));

            return new ProviderIpLocation(country, area, string.Empty);
        }
        catch (AddressNotFoundException)
        {
            return ProviderIpLocation.Empty;
        }
    }

    public DbFileInfo GetFileInfo()
    {
        var fi = new FileInfo(_path);
        return new DbFileInfo(
            Path: _path,
            SizeBytes: fi.Exists ? fi.Length : 0,
            LastUpdatedUtc: _lastWriteTime,
            IsAvailable: IsAvailable);
    }

    public void TryReload()
    {
        var fileState = GetFileState(out var fileInfo);
        if (fileState == DbFileState.Missing)
        {
            if (_current is not null)
            {
                _missingReloadCount++;
                if (_missingReloadCount < MissingFileReloadsBeforeUnavailable)
                {
                    _logger.LogWarning(
                        "MaxMind GeoLite2 City database at {Path} is not visible on reload attempt {Attempt}/{Threshold}. Keeping the current in-memory database.",
                        _path,
                        _missingReloadCount,
                        MissingFileReloadsBeforeUnavailable);
                    return;
                }

                _logger.LogWarning(
                    "MaxMind GeoLite2 City database at {Path} was missing for {MissingReloadCount} consecutive reload attempts. This provider will be unavailable.",
                    _path,
                    _missingReloadCount);
                Interlocked.Exchange(ref _current, null)?.Dispose();
                _lastWriteTime = DateTime.MinValue;
            }
            return;
        }

        if (fileState == DbFileState.Unreadable) return;

        _missingReloadCount = 0;
        if (fileInfo is null) return;
        if (_current is not null && fileInfo.LastWriteTimeUtc == _lastWriteTime) return;

        TryLoad(initialLoad: false);
    }

    private void TryLoad(bool initialLoad)
    {
        var fileState = GetFileState(out var fileInfo);
        if (fileState == DbFileState.Missing)
        {
            Interlocked.Exchange(ref _current, null)?.Dispose();
            _lastWriteTime = DateTime.MinValue;
            _missingReloadCount = 0;
            if (initialLoad)
            {
                _logger.LogWarning(
                    "MaxMind GeoLite2 City database not found or not accessible at {Path}. This provider will be unavailable until the file is provided with readable permissions.",
                    _path);
            }
            return;
        }

        if (fileState == DbFileState.Unreadable) return;

        if (fileInfo is null) return;
        var fileSize = fileInfo.Length;
        var lastWriteTime = fileInfo.LastWriteTimeUtc;

        if (fileSize < MinValidFileSizeBytes)
        {
            _logger.LogWarning(
                "MaxMind GeoLite2 City database at {Path} is only {Size} bytes - skipping reload, likely mid-write.",
                _path, fileSize);
            return;
        }

        try
        {
            var newDb = new DatabaseReader(_path);
            var oldDb = Interlocked.Exchange(ref _current, newDb);
            oldDb?.Dispose();
            _lastWriteTime = lastWriteTime;
            _missingReloadCount = 0;

            if (!initialLoad)
            {
                _logger.LogInformation("MaxMind GeoLite2 City database reloaded from {Path} ({Size:N0} bytes)", _path, fileSize);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(
                ex,
                "Failed to load MaxMind GeoLite2 City database from {Path}. Keeping the current database state.",
                _path);
        }
    }

    private DbFileState GetFileState(out FileInfo? fileInfo)
    {
        try
        {
            fileInfo = new FileInfo(_path);
            if (!fileInfo.Exists)
            {
                return DbFileState.Missing;
            }

            _ = fileInfo.Length;
            _ = fileInfo.LastWriteTimeUtc;
            return DbFileState.Exists;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            fileInfo = null;
            _logger.LogWarning(
                ex,
                "Unable to read MaxMind GeoLite2 City database metadata at {Path}. This provider will keep the current database state.",
                _path);
            return DbFileState.Unreadable;
        }
    }

    private string ReadLocalizedName(
        IReadOnlyDictionary<string, string>? names,
        string? defaultName,
        string? fallbackCode)
    {
        if (names is not null)
        {
            if (names.TryGetValue(_locale, out var localizedName) && !string.IsNullOrWhiteSpace(localizedName))
            {
                return localizedName;
            }

            if (names.TryGetValue("en", out var englishName) && !string.IsNullOrWhiteSpace(englishName))
            {
                return englishName;
            }

            var firstName = names.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                return firstName;
            }
        }

        return defaultName ?? fallbackCode ?? string.Empty;
    }
}
