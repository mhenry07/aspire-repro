using System.Text;
using AspireRepro.Resource;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

/// <summary>
/// Reads and processes the response stream using a buffer without using System.IO.Pipelines or
/// <see cref="MediaDownloader"/>
/// </summary>
/// <remarks>
/// <see cref="ReaderType.FillBufferReader"/> uses an algorithm similar to <see cref="MediaDownloader"/> and
/// triggers the issue (see <see cref="FillPipeAsync"/>)
/// </remarks>
public class BufferReader(
    IHostApplicationLifetime lifetime, ILogger<PipeBuffer> logger, IOptions<ReadOptions> options,
    ResourceClient resourceClient)
{
    private readonly int _batchSize = options.Value.BatchSize ?? 100;
    private readonly int _chunkSize = options.Value.ChunkSize ?? 81_920;
    private readonly TimeSpan _delay = options.Value.IoDelay ?? TimeSpan.FromSeconds(15);
    private readonly bool _fillBuffer = options.Value.ReaderType == ReaderType.FillBufferReader;

    public async Task ReadAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[_chunkSize];
        var bytesConsumed = 0L;
        var offset = 0;
        var row = 0L;

        try
        {
            var response = await resourceClient.HttpClient
                .GetAsync("/get", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                var length = _fillBuffer
                    ? await ReadUntilFullAsync(responseStream, buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false)
                    : await ReadOnceAsync(responseStream, buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);

                if (offset + length == 0)
                    break;

                var memory = buffer.AsMemory(0, offset + length);
                while (TryReadLine(ref memory, out var line))
                {
                    bytesConsumed += line.Length + 1;
                    await ProcessLineAsync(line, row, bytesConsumed).ConfigureAwait(false);
                    row++;

                    if (row % 10_000 == 0)
                        logger.LogInformation("{ReaderType}: Processed {Count:N0} lines, ~{BytesConsumed:N0} bytes", options.Value.ReaderType, row, bytesConsumed);
                }

                if (memory.IsEmpty)
                {
                    offset = 0;
                }
                else
                {
                    memory.CopyTo(buffer);
                    offset = memory.Length;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "{ReaderType} failed, stopping application", options.Value.ReaderType);
            lifetime.StopApplication();
        }

        static ValueTask<int> ReadOnceAsync(Stream s, Memory<byte> b, CancellationToken ct)
            => s.ReadAsync(b, ct);

        // implementation for ReaderType: PipeFillBuffer, similar to MediaDownloader algorithm, triggers issue
        static async ValueTask<int> ReadUntilFullAsync(Stream s, Memory<byte> b, CancellationToken ct)
        {
            var count = 0;
            while (count < b.Length)
            {
                var read = await s.ReadAsync(b[count..], ct).ConfigureAwait(false);
                if (read == 0)
                    break;

                count += read;
            }

            return count;
        }
    }

    private async Task ProcessLineAsync(ReadOnlyMemory<byte> line, long row, long bytes)
    {
        ProcessLineCore(line.Span, row, bytes);

        if (row > 0 && row % _batchSize == 0)
            await ProcessBatchAsync();
    }

    private Task ProcessBatchAsync()
        => Task.WhenAll(Enumerable.Range(0, _batchSize).Select(_ => Task.Delay(_delay)));

    private void ProcessLineCore(ReadOnlySpan<byte> line, long row, long bytes)
    {
        if (line.IsEmpty)
            return;

        if (line.Length > Formatter.MaxLineLength)
            throw new InvalidOperationException($"Length exceeded max size at row {row:N0}, ~{bytes:N0} bytes: {Encoding.UTF8.GetString(line)}");

        Span<byte> buffer = stackalloc byte[(int)line.Length];
        Span<byte> expected = stackalloc byte[Formatter.MaxLineLength];
        var expectedLength = Formatter.Format(expected, row, eol: false);
        expected = expected[..expectedLength];

        if (line.TryCopyTo(buffer) && expected.SequenceEqual(buffer))
            return;

        var lineText = Encoding.UTF8.GetString(line);
        var expectedText = Encoding.UTF8.GetString(expected[..expectedLength]);
        logger.LogError("{ReaderType}: Line was corrupted at row {Row:N0}, ~{BytesConsumed:N0} bytes:\nActual:   '{Actual}'\nExpected: '{Expected}'", options.Value.ReaderType, row, bytes, lineText, expectedText);
        throw new InvalidOperationException($"Line was corrupted at row {row:N0}, ~{bytes:N0} bytes: '{lineText}'");
    }

    private static bool TryReadLine(ref Memory<byte> buffer, out ReadOnlyMemory<byte> line)
    {
        var span = buffer.Span;

        // Look for a EOL in the buffer.
        var index = span.IndexOf((byte)'\n');
        if (index == -1)
        {
            line = null;
            return false;
        }

        // Skip the line + the \n.
        line = buffer[..index];
        buffer = buffer[(index + 1)..];
        return true;
    }
}
