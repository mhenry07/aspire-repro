using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using AspireRepro.Resource;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

/// <summary>
/// Reads and processes the response using System.IO.Pipelines and <see cref="HttpContent.CopyToAsync"/> without using
/// <see cref="MediaDownloader"/>
/// </summary>
/// <remarks>
/// Appears to avoid the issue that occurs when using System.IO.Pipelines with MediaDownloader
/// </remarks>
public class PipeCopyTo(
    IHostApplicationLifetime lifetime, ILogger<PipeCopyTo> logger, IOptions<ReadOptions> options,
    ResourceClient resourceClient)
{
    private readonly int _batchSize = options.Value.BatchSize ?? 100;
    private readonly int _chunkSize = options.Value.ChunkSize ?? 81_920;
    private readonly TimeSpan _delay = options.Value.IoDelay ?? TimeSpan.FromSeconds(15);

    public async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pipe = new Pipe();

            var writeTask = FillPipeAsync(pipe.Writer, cancellationToken);
            var readTask = ReadPipeAsync(pipe.Reader, cancellationToken);

            await Task.WhenAll(readTask, writeTask);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, $"{nameof(PipeCopyTo)} failed, stopping application");
            lifetime.StopApplication();
        }
    }

    private async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        using var stream = writer.AsStream();
        try
        {
            var response = await resourceClient.HttpClient
                .GetAsync("/get", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await response.Content.CopyToAsync(stream, cancellationToken);

            await writer.CompleteAsync();
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex);
        }
    }

    private async Task ReadPipeAsync(PipeReader reader, CancellationToken cancellationToken)
    {
        var bytesConsumed = 0L;
        var row = 0L;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(default);
                var buffer = result.Buffer;
                var start = buffer.Start;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    var bytes = bytesConsumed + result.Buffer.Slice(start, buffer.Start).Length;
                    await ProcessLineAsync(line, row, bytes);

                    row++;
                }

                bytesConsumed += result.Buffer.Slice(start, buffer.Start).Length;
                reader.AdvanceTo(buffer.Start, buffer.End);
                logger.LogInformation(nameof(PipeCopyTo) + ": Advanced reader at row {Row:N0}, ~{BytesConsumed:N0} bytes", row, bytesConsumed);

                if (result.IsCompleted)
                    break;
            }

            await reader.CompleteAsync();
        }
        catch (Exception ex)
        {
            await reader.CompleteAsync(ex);
            throw;
        }
    }

    private async Task ProcessLineAsync(ReadOnlySequence<byte> line, long row, long bytes)
    {
        ProcessLineCore(line, row, bytes);

        if (row > 0 && row % _batchSize == 0)
            await ProcessBatchAsync();
    }

    private Task ProcessBatchAsync()
        => Task.WhenAll(Enumerable.Range(0, _batchSize).Select(_ => Task.Delay(_delay)));

    private void ProcessLineCore(ReadOnlySequence<byte> line, long row, long bytes)
    {
        if (line.IsEmpty)
            return;

        if (line.Length > Formatter.MaxLineLength)
            throw new InvalidOperationException($"Length exceeded max size at row {row:N0}, ~{bytes:N0} bytes: {Encoding.UTF8.GetString(line)}");

        Span<byte> buffer = stackalloc byte[(int)line.Length];
        Span<byte> expected = stackalloc byte[Formatter.MaxLineLength];
        var expectedLength = Formatter.Format(expected, row, eol: false);
        expected = expected[..expectedLength];

        var reader = new SequenceReader<byte>(line);
        if (reader.TryCopyTo(buffer) && expected.SequenceEqual(buffer))
            return;

        var lineText = Encoding.UTF8.GetString(line);
        var expectedText = Encoding.UTF8.GetString(expected[..expectedLength]);
        logger.LogError(nameof(PipeCopyTo) + ": Line was corrupted at row {Row:N0}, ~{BytesConsumed:N0} bytes:\nActual:   '{Actual}'\nExpected: '{Expected}'", row, bytes, lineText, expectedText);
        throw new InvalidOperationException($"Line was corrupted at row {row:N0}, ~{bytes:N0} bytes: '{lineText}'");
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        // Look for a EOL in the buffer.
        SequencePosition? position = buffer.PositionOf((byte)'\n');

        if (position == null)
        {
            line = default;
            return false;
        }

        // Skip the line + the \n.
        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }
}
