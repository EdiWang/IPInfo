using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IPInfo.Services;

public sealed record IpLocation(string Country, string Area);

public sealed class QqwryDb
{
    private readonly byte[] _data;
    private readonly long _indexStart;
    private readonly long _indexEnd;
    private readonly Encoding _enc;

    public QqwryDb(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _enc = Encoding.GetEncoding(936);
        _data = File.ReadAllBytes(path);

        _indexStart = ReadUInt32LE(0);
        _indexEnd = ReadUInt32LE(4);
    }

    public IpLocation Query(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork)
            return new IpLocation(string.Empty, string.Empty);

        uint ipNum = IpToUInt32(ip);

        long index = FindIndex(ipNum);
        if (index < 0)
            return new IpLocation(string.Empty, string.Empty);

        long pos = index + 4; // skip startIP (4 bytes)
        uint recordOffset = ReadUInt24LE(pos);

        // skip endIP (4 bytes) at recordOffset
        long recordPos = recordOffset + 4;

        var (country, area) = ReadLocationStrings(recordPos);
        country = Normalize(country);
        area = Normalize(area);
        return new IpLocation(country, area);
    }

    private (string country, string area) ReadLocationStrings(long pos)
    {
        byte mode = _data[pos];

        if (mode == 0x01)
        {
            uint p = ReadUInt24LE(pos + 1);
            return ReadLocationStrings(p);
        }

        if (mode == 0x02)
        {
            uint countryOffset = ReadUInt24LE(pos + 1);
            string country = ReadCStringAt(countryOffset);

            long areaPos = pos + 4; // 1(mode) + 3(offset)
            string area = ReadAreaString(areaPos);
            return (country, area);
        }

        // normal: country string then area string
        string countryStr = ReadCStringAt(pos);
        long nextPos = pos + GetCStringByteLength(pos) + 1; // +1 for null terminator
        string areaStr = ReadAreaString(nextPos);
        return (countryStr, areaStr);
    }

    private string ReadAreaString(long pos)
    {
        byte mode = _data[pos];
        if (mode is 0x01 or 0x02)
        {
            uint p = ReadUInt24LE(pos + 1);
            if (p == 0) return string.Empty;
            return ReadCStringAt(p);
        }

        return ReadCStringAt(pos);
    }

    private long FindIndex(uint ipNum)
    {
        long left = 0;
        long right = (_indexEnd - _indexStart) / 7;

        while (left <= right)
        {
            long mid = (left + right) / 2;
            long pos = _indexStart + mid * 7;

            uint startIp = ReadUInt32LE(pos);
            uint recordOffset = ReadUInt24LE(pos + 4);
            uint endIp = ReadUInt32LE(recordOffset);

            if (ipNum < startIp)
                right = mid - 1;
            else if (ipNum > endIp)
                left = mid + 1;
            else
                return pos;
        }

        return -1;
    }

    private string ReadCStringAt(long offset)
    {
        int start = (int)offset;
        int end = start;
        while (end < _data.Length && _data[end] != 0)
        {
            end++;
        }

        if (end == start) return string.Empty;
        return _enc.GetString(_data, start, end - start);
    }

    private int GetCStringByteLength(long offset)
    {
        int start = (int)offset;
        int end = start;
        while (end < _data.Length && _data[end] != 0)
        {
            end++;
        }

        return end - start;
    }

    private uint ReadUInt24LE(long pos)
    {
        return (uint)(_data[pos] | (_data[pos + 1] << 8) | (_data[pos + 2] << 16));
    }

    private uint ReadUInt32LE(long pos)
    {
        return (uint)(_data[pos] | (_data[pos + 1] << 8) | (_data[pos + 2] << 16) | (_data[pos + 3] << 24));
    }

    private static uint IpToUInt32(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes(); // big-endian (network order)
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        if (s.Contains("CZ88.NET", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return s.Trim();
    }
}
