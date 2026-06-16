using IPInfo.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Xunit;

namespace IPInfo.Tests;

public sealed class QqwryDbProviderTests
{
    [Fact]
    public void Constructor_KeepsDatabaseUnavailable_WhenFileIsMissing()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");

        var provider = new QqwryDbProvider(path, NullLogger<QqwryDbProvider>.Instance);

        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Constructor_KeepsDatabaseUnavailable_WhenFileIsTooSmall()
    {
        using var file = TestQqwryDatabase.WriteTempFile([1, 2, 3]);

        var provider = new QqwryDbProvider(file.Path, NullLogger<QqwryDbProvider>.Instance);

        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void Constructor_LoadsDatabase_WhenFilePassesProviderValidation()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));

        var provider = new QqwryDbProvider(file.Path, NullLogger<QqwryDbProvider>.Instance);

        var result = provider.Query(IPAddress.Parse("1.1.1.8"));

        Assert.True(provider.IsAvailable);
        Assert.Equal("United States", result.Country);
        Assert.Equal("Google LLC", result.Area);
    }

    [Fact]
    public void TryReload_KeepsCurrentDatabase_WhenReplacementFileIsTooSmall()
    {
        using var file = TestQqwryDatabase.WriteTempFile(
            TestQqwryDatabase.CreateNormalDb("United States", "Google LLC", padToProviderMinimum: true));
        var provider = new QqwryDbProvider(file.Path, NullLogger<QqwryDbProvider>.Instance);

        File.WriteAllBytes(file.Path, [1, 2, 3]);
        provider.TryReload();

        var result = provider.Query(IPAddress.Parse("1.1.1.8"));
        Assert.True(provider.IsAvailable);
        Assert.Equal("United States", result.Country);
        Assert.Equal("Google LLC", result.Area);
    }
}
