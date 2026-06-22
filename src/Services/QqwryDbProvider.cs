using System.Net;

namespace IPInfo.Services;

public sealed class QqwryDbProvider
{
    private enum DbFileState
    {
        Exists,
        Missing,
        Unreadable
    }

    private volatile QqwryDb? _current;
    private readonly string _path;
    private DateTime _lastWriteTime;
    private int _missingReloadCount;
    private readonly ILogger<QqwryDbProvider> _logger;

    // A valid QQWry.dat is several MB; reject anything suspiciously small
    // to guard against reading a partially-written file.
    private const long MinValidFileSizeBytes = 1 * 1024 * 1024; // 1 MB
    private const int MissingFileReloadsBeforeUnavailable = 3;

    public bool IsAvailable => _current is not null;

    public QqwryDbProvider(string path, ILogger<QqwryDbProvider> logger)
    {
        _path = path;
        _logger = logger;
        TryLoad(initialLoad: true);
    }

    public IpLocation Query(IPAddress ip)
    {
        var db = _current ?? throw new InvalidOperationException(
            $"IP database not found at the configured path '{_path}'. Please check the configuration and ensure the database file exists.");
        return db.Query(ip);
    }

    public DbFileInfo GetFileInfo()
    {
        var fi = new FileInfo(_path);
        return new DbFileInfo(
            Path: _path,
            SizeBytes: fi.Exists ? fi.Length : 0,
            LastUpdatedUtc: _lastWriteTime
        );
    }

    internal void TryReload()
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
                        "QQWry database at {Path} is not visible on reload attempt {Attempt}/{Threshold}. Keeping the current in-memory database.",
                        _path,
                        _missingReloadCount,
                        MissingFileReloadsBeforeUnavailable);
                    return;
                }

                _logger.LogWarning(
                    "QQWry database at {Path} was missing for {MissingReloadCount} consecutive reload attempts. IP lookup will be unavailable.",
                    _path,
                    _missingReloadCount);
                _current = null;
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
            _current = null;
            _lastWriteTime = DateTime.MinValue;
            _missingReloadCount = 0;
            if (initialLoad)
            {
                _logger.LogWarning(
                    "QQWry database not found at {Path}. IP lookup will be unavailable until the file is provided.",
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
                "QQWry database at {Path} is only {Size} bytes — skipping reload, likely mid-write.",
                _path, fileSize);
            return; // _lastWriteTime not updated → will retry next poll
        }

        try
        {
            var newDb = new QqwryDb(_path);
            Interlocked.Exchange(ref _current, newDb);
            _lastWriteTime = lastWriteTime;
            _missingReloadCount = 0;

            if (!initialLoad)
            {
                _logger.LogInformation("QQWry database reloaded from {Path} ({Size:N0} bytes)", _path, fileSize);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(
                ex,
                "Failed to load QQWry database from {Path}. Keeping the current database state.",
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
                "Unable to read QQWry database metadata at {Path}. IP lookup will keep the current database state.",
                _path);
            return DbFileState.Unreadable;
        }
    }
}

public record DbFileInfo(string Path, long SizeBytes, DateTime LastUpdatedUtc);
