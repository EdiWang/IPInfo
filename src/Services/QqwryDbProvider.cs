using System.Net;

namespace IPInfo.Services;

public sealed class QqwryDbProvider
{
    private volatile QqwryDb _current;
    private readonly string _path;
    private DateTime _lastWriteTime;
    private readonly ILogger<QqwryDbProvider> _logger;

    // A valid QQWry.dat is several MB; reject anything suspiciously small
    // to guard against reading a partially-written file.
    private const long MinValidFileSizeBytes = 1 * 1024 * 1024; // 1 MB

    public QqwryDbProvider(string path, ILogger<QqwryDbProvider> logger)
    {
        _path = path;
        _logger = logger;
        _current = new QqwryDb(path);
        _lastWriteTime = File.GetLastWriteTimeUtc(path);
    }

    public IpLocation Query(IPAddress ip) => _current.Query(ip);

    internal void TryReload()
    {
        var newWriteTime = File.GetLastWriteTimeUtc(_path);
        if (newWriteTime == _lastWriteTime) return;

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
