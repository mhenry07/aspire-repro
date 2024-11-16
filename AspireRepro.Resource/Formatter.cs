using System.Buffers.Text;
using System.Text;

namespace AspireRepro.Resource;

public static class Formatter
{
    public const int MaxLineLength = 128;
    private const int TicksPerMs = 10_000;
    private const int TicksPerSec = 1_000 * TicksPerMs;

    // $"abc,{row},def,{new string('a', row % 13 + 1)},ghi,{GetTime1(row)},jkl,{GetTime2(row)},mno"
    public static int Format(Span<byte> buffer, long row, bool eol)
    {
        var written = Encoding.UTF8.GetBytes("abc,", buffer);
        Utf8Formatter.TryFormat(row, buffer[written..], out var bytes);
        written += bytes;
        written += Encoding.UTF8.GetBytes(",def,", buffer[written..]);
        for (var i = 1; i <= row % 13; i++)
            buffer[written++] = (byte)'a';
        written += Encoding.UTF8.GetBytes(",ghi,", buffer[written..]);
        Utf8Formatter.TryFormat(GetTime1(row), buffer[written..], out bytes);
        written += bytes;
        written += Encoding.UTF8.GetBytes(",jkl,", buffer[written..]);
        Utf8Formatter.TryFormat(GetTime2(row), buffer[written..], out bytes);
        written += bytes;
        written += Encoding.UTF8.GetBytes(",mno", buffer[written..]);

        if (eol)
            buffer[written++] = (byte)'\n';

        return written;
    }

    public static DateTimeOffset GetTime1(long row) => new(row * TicksPerMs, TimeSpan.Zero);
    public static DateTimeOffset GetTime2(long row) => new(row * TicksPerSec, TimeSpan.Zero);
}
