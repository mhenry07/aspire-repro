﻿using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using AspireRepro.Resource;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

/// <summary>
/// Reads and processes the response using System.IO.Pipelines and a buffer without using <see cref="MediaDownloader"/>
/// </summary>
/// <remarks>
/// <see cref="ReaderType.PipeFillBuffer"/> uses an algorithm similar to <see cref="MediaDownloader"/> and
/// triggers the issue (see <see cref="FillPipeAsync"/>)
/// </remarks>
public class PipeBuffer(
    IHostApplicationLifetime lifetime, ILogger<PipeBuffer> logger, IOptions<ReadOptions> options,
    ResourceClient resourceClient)
{
    private readonly int _batchSize = options.Value.BatchSize ?? 100;
    private readonly int _chunkSize = options.Value.ChunkSize ?? 81_920;
    private readonly TimeSpan _delay = options.Value.IoDelay ?? TimeSpan.FromSeconds(15);
    private readonly bool _fillBuffer = options.Value.ReaderType == ReaderType.PipeFillBuffer;

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
            logger.LogCritical(ex, "{ReaderType} failed, stopping application", options.Value.ReaderType);
            lifetime.StopApplication();
        }
    }

    private async Task FillPipeAsync(PipeWriter writer, CancellationToken cancellationToken)
    {
        var buffer = new byte[_chunkSize];
        using var writerStream = writer.AsStream();
        try
        {
            var response = await resourceClient.HttpClient
                .GetAsync("/get", HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                var length = _fillBuffer
                    ? await ReadUntilFullAsync(responseStream, buffer, cancellationToken).ConfigureAwait(false)
                    : await ReadOnceAsync(responseStream, buffer, cancellationToken).ConfigureAwait(false);

                if (length == 0)
                    break;

                await writerStream.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            }

            await writer.CompleteAsync();
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex);
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
                logger.LogInformation("{ReaderType}: Advanced reader at row {Row:N0}, ~{BytesConsumed:N0} bytes", options.Value.ReaderType, row, bytesConsumed);

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
        logger.LogError("{ReaderType}: Line was corrupted at row {Row:N0}, ~{BytesConsumed:N0} bytes:\nActual:   '{Actual}'\nExpected: '{Expected}'", options.Value.ReaderType, row, bytes, lineText, expectedText);
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