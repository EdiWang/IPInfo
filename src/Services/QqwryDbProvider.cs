using System.Net;

namespace IPInfo.Services;

public sealed class QqwryDbProvider : IDisposable
{
    private volatile QqwryDb _current;
    private readonly string _path;
    private DateTime _lastWriteTime;
    private readonly Timer _timer;
    private readonly ILogger<QqwryDbProvider> _logger;

    public QqwryDbProvider(string path, TimeSpan pollInterval, ILogger<QqwryDbProvider> logger)
    {
        _path = path;
        _logger = logger;
        _current = new QqwryDb(path);
        _lastWriteTime = File.GetLastWriteTimeUtc(path);
        _timer = new Timer(CheckForUpdate, null, pollInterval, pollInterval);
    }

    public IpLocation Query(IPAddress ip) => _current.Query(ip);

    // A valid QQWry.dat is several MB; reject anything suspiciously small
    // to guard against reading a partially-written file.
    private const long MinValidFileSizeBytes = 1 * 1024 * 1024; // 1 MB

    private void CheckForUpdate(object? state)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reload QQWry database from {Path}", _path);
        }
    }

    public void Dispose() => _timer.Dispose();
}
