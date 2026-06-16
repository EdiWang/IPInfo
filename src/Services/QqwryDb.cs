using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IPInfo.Services;

public sealed record IpLocation(string Country, string Area);

public sealed class QqwryDb
{
    private const int HeaderLength = 8;
    private const int IndexRecordLength = 7;
    private const int MaxRedirectDepth = 8;

    private readonly byte[] _data;
    private readonly long _indexStart;
    private readonly long _indexEnd;
    private readonly Encoding _enc;

    public QqwryDb(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _enc = Encoding.GetEncoding(936);
        _data = File.ReadAllBytes(path);

        if (!TryReadUInt32LE(0, out var indexStart) || !TryReadUInt32LE(4, out var indexEnd))
        {
            throw new InvalidDataException("QQWry database header is incomplete.");
        }

        _indexStart = indexStart;
        _indexEnd = indexEnd;

        if (_data.Length < HeaderLength ||
            _indexStart > _indexEnd ||
            (_indexEnd - _indexStart) % IndexRecordLength != 0 ||
            !HasBytes(_indexStart, IndexRecordLength) ||
            !HasBytes(_indexEnd, IndexRecordLength))
        {
            throw new InvalidDataException("QQWry database index range is invalid.");
        }
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
        if (!TryReadUInt24LE(pos, out var recordOffset))
            return new IpLocation(string.Empty, string.Empty);

        // skip endIP (4 bytes) at recordOffset
        long recordPos = recordOffset + 4;

        var (country, area) = ReadLocationStrings(recordPos, depth: 0);
        country = Normalize(country);
        area = Normalize(area);
        return new IpLocation(country, area);
    }

    private (string country, string area) ReadLocationStrings(long pos, int depth)
    {
        if (depth > MaxRedirectDepth || !TryReadByte(pos, out var mode))
            return (string.Empty, string.Empty);

        if (mode == 0x01)
        {
            if (!TryReadUInt24LE(pos + 1, out var p) || p == 0)
                return (string.Empty, string.Empty);

            return ReadLocationStrings(p, depth + 1);
        }

        if (mode == 0x02)
        {
            if (!TryReadUInt24LE(pos + 1, out var countryOffset))
                return (string.Empty, string.Empty);

            string country = ReadCStringAt(countryOffset);

            long areaPos = pos + 4; // 1(mode) + 3(offset)
            string area = ReadAreaString(areaPos, depth);
            return (country, area);
        }

        // normal: country string then area string
        string countryStr = ReadCStringAt(pos);
        if (!TryGetCStringByteLength(pos, out var countryByteLength))
            return (countryStr, string.Empty);

        long nextPos = pos + countryByteLength + 1; // +1 for null terminator
        string areaStr = ReadAreaString(nextPos, depth);
        return (countryStr, areaStr);
    }

    private string ReadAreaString(long pos, int depth)
    {
        if (depth > MaxRedirectDepth || !TryReadByte(pos, out var mode))
            return string.Empty;

        if (mode is 0x01 or 0x02)
        {
            if (!TryReadUInt24LE(pos + 1, out var p))
                return string.Empty;

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

            if (!TryReadUInt32LE(pos, out var startIp) ||
                !TryReadUInt24LE(pos + 4, out var recordOffset) ||
                !TryReadUInt32LE(recordOffset, out var endIp))
            {
                return -1;
            }

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
        if (!TryGetCStringByteLength(offset, out var length))
            return string.Empty;

        if (length == 0) return string.Empty;
        return _enc.GetString(_data, (int)offset, length);
    }

    private bool TryGetCStringByteLength(long offset, out int length)
    {
        length = 0;
        if (!HasBytes(offset, 1))
            return false;

        int start = (int)offset;
        int end = start;
        while (end < _data.Length && _data[end] != 0)
        {
            end++;
        }

        if (end >= _data.Length)
            return false;

        length = end - start;
        return true;
    }

    private bool TryReadByte(long pos, out byte value)
    {
        value = 0;
        if (!HasBytes(pos, 1))
            return false;

        value = _data[(int)pos];
        return true;
    }

    private bool TryReadUInt24LE(long pos, out uint value)
    {
        value = 0;
        if (!HasBytes(pos, 3))
            return false;

        var index = (int)pos;
        value = (uint)(_data[index] | (_data[index + 1] << 8) | (_data[index + 2] << 16));
        return true;
    }

    private bool TryReadUInt32LE(long pos, out uint value)
    {
        value = 0;
        if (!HasBytes(pos, 4))
            return false;

        var index = (int)pos;
        value = (uint)(_data[index] | (_data[index + 1] << 8) | (_data[index + 2] << 16) | (_data[index + 3] << 24));
        return true;
    }

    private bool HasBytes(long offset, int count)
    {
        return offset >= 0 && count >= 0 && offset <= _data.Length - count;
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
