using IPInfo.Services;
using System.Net;
using System.Text;
using Xunit;

namespace IPInfo.Tests;

public sealed class QqwryDbTests
{
    [Fact]
    public void Query_ReturnsLocation_WhenRecordUsesNormalStrings()
    {
        using var file = new TempQqwryFile(CreateNormalDb("United States", "Google LLC"));

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal("United States", result.Country);
        Assert.Equal("Google LLC", result.Area);
    }

    [Fact]
    public void Constructor_ThrowsInvalidDataException_WhenHeaderIsIncomplete()
    {
        using var file = new TempQqwryFile([1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => new QqwryDb(file.Path));
    }

    [Fact]
    public void Constructor_ThrowsInvalidDataException_WhenIndexRangeIsOutsideFile()
    {
        var data = new byte[16];
        WriteUInt32LE(data, 0, 8);
        WriteUInt32LE(data, 4, 100);
        using var file = new TempQqwryFile(data);

        Assert.Throws<InvalidDataException>(() => new QqwryDb(file.Path));
    }

    [Fact]
    public void Query_ReturnsEmptyLocation_WhenRecordOffsetIsOutsideFile()
    {
        var data = CreateDbWithRecordOffset(1000);
        using var file = new TempQqwryFile(data);

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal(string.Empty, result.Country);
        Assert.Equal(string.Empty, result.Area);
    }

    [Fact]
    public void Query_ReturnsEmptyLocation_WhenLocationRedirectLoops()
    {
        var data = CreateDbWithLoopingLocationRedirect();
        using var file = new TempQqwryFile(data);

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal(string.Empty, result.Country);
        Assert.Equal(string.Empty, result.Area);
    }

    [Fact]
    public void Query_ReturnsEmptyLocation_WhenLocationStringIsUnterminated()
    {
        var data = CreateDbWithRecordOffset(15, length: 24);
        Array.Fill(data, (byte)'X', 19, data.Length - 19);
        using var file = new TempQqwryFile(data);

        var result = new QqwryDb(file.Path).Query(IPAddress.Parse("1.1.1.8"));

        Assert.Equal(string.Empty, result.Country);
        Assert.Equal(string.Empty, result.Area);
    }

    private static byte[] CreateNormalDb(string country, string area)
    {
        var data = CreateDbWithRecordOffset(15, length: 128);
        var pos = 19;
        WriteAsciiCString(data, ref pos, country);
        WriteAsciiCString(data, ref pos, area);
        return data[..pos];
    }

    private static byte[] CreateDbWithLoopingLocationRedirect()
    {
        var data = CreateDbWithRecordOffset(15, length: 32);
        data[19] = 0x01;
        WriteUInt24LE(data, 20, 19);
        return data;
    }

    private static byte[] CreateDbWithRecordOffset(uint recordOffset, int length = 19)
    {
        var data = new byte[length];
        WriteUInt32LE(data, 0, 8);
        WriteUInt32LE(data, 4, 8);
        WriteUInt32LE(data, 8, IpToUInt32("1.1.1.0"));
        WriteUInt24LE(data, 12, recordOffset);

        if (recordOffset <= data.Length - 4)
        {
            WriteUInt32LE(data, (int)recordOffset, IpToUInt32("1.1.1.255"));
        }

        return data;
    }

    private static void WriteAsciiCString(byte[] data, ref int offset, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        bytes.CopyTo(data, offset);
        offset += bytes.Length;
        data[offset++] = 0;
    }

    private static void WriteUInt24LE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
    }

    private static void WriteUInt32LE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static uint IpToUInt32(string ip)
    {
        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }

    private sealed class TempQqwryFile : IDisposable
    {
        public TempQqwryFile(byte[] data)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.dat");
            File.WriteAllBytes(Path, data);
        }

        public string Path { get; }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }
}
