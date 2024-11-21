using System.Text;
using AspireRepro.Resource;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

/// <summary>
/// Reads and processes the response stream using a buffer without using System.IO.Pipelines or
/// <see cref="MediaDownloader"/> and triggers the issue
/// </summary>
/// <remarks>
/// <see cref="ReaderType.FillBufferReferenceStream"/> uses an algorithm similar to <see cref="MediaDownloader"/>
/// </remarks>
public class BufferReferenceStream(
    IHostApplicationLifetime lifetime, ILogger<BufferReferenceStream> logger, IOptions<ReadOptions> options,
    ResourceClient resourceClient)
{
    private readonly int _batchSize = options.Value.BatchSize ?? 100;
    private readonly int _chunkSize = options.Value.ChunkSize ?? 81_920;
    private readonly TimeSpan _delay = options.Value.IoDelay ?? TimeSpan.FromSeconds(15);
    private readonly bool _fillBuffer = options.Value.ReaderType == ReaderType.FillBufferReferenceStream;

    public async Task ReadAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[_chunkSize];
        var referenceBuffer = new byte[_chunkSize];
        var bytesConsumed = 0L;
        var offset = 0;
        var row = 0L;

        try
        {
            var response = await resourceClient.HttpClient
                .GetAsync("/get", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            using var referenceStream = new ReadWriteStream();
            var referenceTask = Task.Run(() => ProduceReferenceAsync(referenceStream, cancellationToken), cancellationToken);
            await Task.Delay(1_000, cancellationToken);

            while (true)
            {
                var length = _fillBuffer
                    ? await ReadUntilFullAsync(responseStream, buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false)
                    : await ReadOnceAsync(responseStream, buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);

                var referenceLength = _fillBuffer
                    ? await ReadUntilFullAsync(responseStream, referenceBuffer.AsMemory(offset), cancellationToken).ConfigureAwait(false)
                    : await referenceStream.ReadAsync(referenceBuffer.AsMemory(offset, length), cancellationToken).ConfigureAwait(false);

                if (offset + length == 0)
                    break;

                var memory = buffer.AsMemory(0, offset + length);
                var referenceMemory = referenceBuffer.AsMemory(0, offset + referenceLength);
                while (TryReadLine(ref memory, out var line))
                {
                    bytesConsumed += line.Length + 1;
                    if (!TryReadLine(ref referenceMemory, out var referenceLine) || !line.Span.SequenceEqual(referenceLine.Span))
                    {
                        var expected = new byte[Formatter.MaxLineLength];
                        var expectedLength = Formatter.Format(expected, row, eol: false);
                        var expectedText = Encoding.UTF8.GetString(expected.AsSpan(0, expectedLength));
                        var lineText = Encoding.UTF8.GetString(line.Span);
                        var referenceText = Encoding.UTF8.GetString(referenceLine.Span);
                        logger.LogError("{ReaderType}: Line was corrupted at row {Row:N0}, ~{BytesConsumed:N0} bytes:\nActual:    '{Actual}'\nExpected:  '{Expected}'\nReference: '{Reference}'", options.Value.ReaderType, row, bytesConsumed, lineText, expectedText, referenceText);
                        throw new InvalidOperationException($"Line was corrupted at row {row:N0}, ~{bytesConsumed:N0} bytes: '{lineText}'");
                    }

                    await ProcessLineAsync(line, referenceLine, row, bytesConsumed).ConfigureAwait(false);
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
                    referenceMemory.CopyTo(referenceBuffer);
                    offset = memory.Length;
                }
            }

            await referenceTask.ConfigureAwait(false);
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

    private static async Task ProduceReferenceAsync(ReadWriteStream stream, CancellationToken cancellationToken)
    {
        const int maxRows = 20_000_000;
        var line = new byte[Formatter.MaxLineLength];
        var row = 0L;
        while (row < maxRows)
        {
            var length = Formatter.Format(line, row, eol: true);
            var split = (int)(row % (length / 2)) + 1;
            await stream.WriteAsync(line.AsMemory(0, split), cancellationToken);
            await stream.WriteAsync(line.AsMemory(split, length - split), cancellationToken);

            row++;
        }

        stream.EndOfStream = true;
    }

    private async Task ProcessLineAsync(ReadOnlyMemory<byte> line, ReadOnlyMemory<byte> reference, long row, long bytes)
    {
        ProcessLineCore(line.Span, reference.Span, row, bytes);

        if (row > 0 && row % _batchSize == 0)
            await ProcessBatchAsync();
    }

    private Task ProcessBatchAsync()
        => Task.WhenAll(Enumerable.Range(0, _batchSize).Select(_ => Task.Delay(_delay)));

    private void ProcessLineCore(ReadOnlySpan<byte> line, ReadOnlySpan<byte> reference, long row, long bytes)
    {
        if (line.IsEmpty)
            return;

        if (line.Length > Formatter.MaxLineLength)
            throw new InvalidOperationException($"Length exceeded max size at row {row:N0}, ~{bytes:N0} bytes: {Encoding.UTF8.GetString(line)}");

        Span<byte> buffer = stackalloc byte[line.Length];
        Span<byte> expected = stackalloc byte[Formatter.MaxLineLength];
        var expectedLength = Formatter.Format(expected, row, eol: false);
        expected = expected[..expectedLength];

        if (line.TryCopyTo(buffer) && expected.SequenceEqual(buffer))
            return;

        var lineText = Encoding.UTF8.GetString(line);
        var expectedText = Encoding.UTF8.GetString(expected[..expectedLength]);
        var referenceText = Encoding.UTF8.GetString(reference);
        logger.LogError("{ReaderType}: Line was corrupted at row {Row:N0}, ~{BytesConsumed:N0} bytes:\nActual:    '{Actual}'\nExpected:  '{Expected}'\nReference: '{Reference}'", options.Value.ReaderType, row, bytes, lineText, expectedText, referenceText);
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
