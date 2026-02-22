using System.Net;

namespace IPInfo.Services;

public sealed class QqwryDbProvider
{
    private volatile QqwryDb? _current;
    private readonly string _path;
    private DateTime _lastWriteTime;
    private readonly ILogger<QqwryDbProvider> _logger;

    // A valid QQWry.dat is several MB; reject anything suspiciously small
    // to guard against reading a partially-written file.
    private const long MinValidFileSizeBytes = 1 * 1024 * 1024; // 1 MB

    public bool IsAvailable => _current is not null;

    public QqwryDbProvider(string path, ILogger<QqwryDbProvider> logger)
    {
        _path = path;
        _logger = logger;
        _lastWriteTime = File.GetLastWriteTimeUtc(path);

        if (File.Exists(path))
        {
            _current = new QqwryDb(path);
        }
        else
        {
            _current = null;
            _logger.LogWarning(
                "QQWry database not found at {Path}. IP lookup will be unavailable until the file is provided.",
                path);
        }
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
        var newWriteTime = File.GetLastWriteTimeUtc(_path);

        if (!File.Exists(_path))
        {
            if (_current is not null)
            {
                _logger.LogWarning("QQWry database at {Path} was removed. IP lookup will be unavailable.", _path);
                _current = null;
                _lastWriteTime = newWriteTime;
            }
            return;
        }

        if (_current is not null && newWriteTime == _lastWriteTime) return;

        var fileSize = new FileInfo(_path).Length;
        if (fileSize < MinValidFileSizeBytes)
        {
            _logger.LogWarning(
                "QQWry database at {Path} is only {Size} bytes — skipping reload, likely mid-write.",
                _path, fileSize);
            return; // _lastWriteTime not updated → will retry next poll
        }

        var newDb = new QqwryDb(_path);
        Interlocked.Exchange(ref _current, newDb);
        _lastWriteTime = newWriteTime;
        _logger.LogInformation("QQWry database reloaded from {Path} ({Size:N0} bytes)", _path, fileSize);
    }
}

public record DbFileInfo(string Path, long SizeBytes, DateTime LastUpdatedUtc);
