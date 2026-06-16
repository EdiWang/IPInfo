using System.Net;
using System.Text;

namespace IPInfo.Tests;

internal static class TestQqwryDatabase
{
    public static byte[] CreateNormalDb(string country, string area, bool padToProviderMinimum = false)
    {
        var data = CreateDbWithRecordOffset(15, length: padToProviderMinimum ? 1_048_577 : 128);
        var pos = 19;
        WriteAsciiCString(data, ref pos, country);
        WriteAsciiCString(data, ref pos, area);

        if (padToProviderMinimum)
        {
            return data;
        }

        return data[..pos];
    }

    public static byte[] CreateDbWithLoopingLocationRedirect()
    {
        var data = CreateDbWithRecordOffset(15, length: 32);
        data[19] = 0x01;
        WriteUInt24LE(data, 20, 19);
        return data;
    }

    public static byte[] CreateDbWithRecordOffset(uint recordOffset, int length = 19)
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

    public static TempQqwryFile WriteTempFile(byte[] data)
    {
        return new TempQqwryFile(data);
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
}

internal sealed class TempQqwryFile : IDisposable
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
