using IPInfo.Services;
using System.Net;
using Xunit;

namespace IPInfo.Tests;

public sealed class QqwryDbTests
{
    [Fact]
    public void Query_ReturnsLocation_WhenRecordUsesNormalStrings()
    {
        using var file = TestQqwryDatabase.WriteTempFile(TestQqwryDatabase.CreateNormalDb("United States", "Google LLC"));

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal("United States", result.Country);
        Assert.Equal("Google LLC", result.Area);
    }

    [Fact]
    public void Constructor_ThrowsInvalidDataException_WhenHeaderIsIncomplete()
    {
        using var file = TestQqwryDatabase.WriteTempFile([1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => new QqwryDb(file.Path));
    }

    [Fact]
    public void Constructor_ThrowsInvalidDataException_WhenIndexRangeIsOutsideFile()
    {
        var data = new byte[16];
        data[0] = 8;
        data[4] = 100;
        using var file = TestQqwryDatabase.WriteTempFile(data);

        Assert.Throws<InvalidDataException>(() => new QqwryDb(file.Path));
    }

    [Fact]
    public void Query_ReturnsEmptyLocation_WhenRecordOffsetIsOutsideFile()
    {
        var data = TestQqwryDatabase.CreateDbWithRecordOffset(1000);
        using var file = TestQqwryDatabase.WriteTempFile(data);

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal(string.Empty, result.Country);
        Assert.Equal(string.Empty, result.Area);
    }

    [Fact]
    public void Query_ReturnsEmptyLocation_WhenLocationRedirectLoops()
    {
        var data = TestQqwryDatabase.CreateDbWithLoopingLocationRedirect();
        using var file = TestQqwryDatabase.WriteTempFile(data);

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal(string.Empty, result.Country);
        Assert.Equal(string.Empty, result.Area);
    }

    [Fact]
    public void Query_ReturnsEmptyLocation_WhenLocationStringIsUnterminated()
    {
        var data = TestQqwryDatabase.CreateDbWithRecordOffset(15, length: 24);
        Array.Fill(data, (byte)'X', 19, data.Length - 19);
        using var file = TestQqwryDatabase.WriteTempFile(data);

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal(string.Empty, result.Country);
        Assert.Equal(string.Empty, result.Area);
    }

}
