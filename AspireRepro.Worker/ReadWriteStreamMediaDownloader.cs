using System.Text;
using AspireRepro.Resource;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

/// <summary>
/// Reads and processes the response using <see cref="ReadWriteStream"/>, <see cref="StreamReader"/>, and
/// <see cref="MediaDownloader"/>. It has line logic comparable to <see cref="PipeMediaDownloader"/> but does not
/// appear to trigger the reported issue.
/// </summary>
/// <remarks>
/// This differs from <see cref="StreamReaderMediaDownloader"/> in that it uses <see cref="ReadWriteStream"/> to
/// process lines while downloading the response. It uses a lot of memory since it's backed by a
/// <see cref="MemoryStream"/>.
/// </remarks>
public class ReadWriteStreamMediaDownloader(
    IHostApplicationLifetime lifetime, ILogger<ReadWriteStreamMediaDownloader> logger, IOptions<ReadOptions> options,
    ResourceClient resourceClient)
{
    private readonly int _batchSize = options.Value.BatchSize ?? 100;
    private readonly TimeSpan _delay = options.Value.IoDelay ?? TimeSpan.FromSeconds(15);

    public async Task ReadAsync(CancellationToken cancellationToken)
    {
        var row = 0L;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            using var stream = new ReadWriteStream();
            var mediaDownloader = new MediaDownloader(resourceClient.HttpClient, options);
            var downloadTask = mediaDownloader.DownloadAsync("/get", stream, cts.Token);

            while (stream.WritePosition == 0)
                await Task.Delay(50, cts.Token);

            string? line;
            using var reader = new StreamReader(stream);
            while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
            {
                var bytesConsumed = stream.ReadPosition;
                await ProcessLineAsync(line, row, bytesConsumed);
                row++;

                if (row % 20_000 == 0)
                    logger.LogInformation(nameof(ReadWriteStreamMediaDownloader) + ": Processed {Count:N0} lines, ~{BytesConsumed:N0} bytes", row, bytesConsumed);
            }

            await downloadTask;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, $"{nameof(ReadWriteStreamMediaDownloader)} failed, stopping application");
            cts.Cancel();
            lifetime.StopApplication();
        }
    }

    private async Task ProcessLineAsync(string line, long row, long bytes)
    {
        ProcessLineCore(line, row, bytes);

        if (row > 0 && row % _batchSize == 0)
            await ProcessBatchAsync();
    }

    private Task ProcessBatchAsync()
        => Task.WhenAll(Enumerable.Range(0, _batchSize).Select(_ => Task.Delay(_delay)));

    private void ProcessLineCore(string line, long row, long bytes)
    {
        if (string.IsNullOrEmpty(line))
            return;

        if (line.Length > Formatter.MaxLineLength)
            throw new InvalidOperationException($"Length exceeded max size at row {row:N0}, ~{bytes:N0} bytes: {line}");

        Span<byte> actual = stackalloc byte[line.Length];
        Span<byte> expected = stackalloc byte[Formatter.MaxLineLength];
        var expectedLength = Formatter.Format(expected, row, eol: false);
        expected = expected[..expectedLength];

        var actualLength = Encoding.UTF8.GetBytes(line, actual);
        actual = actual[..actualLength];
        if (expected.SequenceEqual(actual))
            return;

        var expectedText = Encoding.UTF8.GetString(expected[..expectedLength]);
        logger.LogError(nameof(ReadWriteStreamMediaDownloader) + ": Line was corrupted at row {Row:N0}, ~{BytesConsumed:N0} bytes:\nActual:   '{Actual}'\nExpected: '{Expected}'", row, bytes, line, expectedText);
        throw new InvalidOperationException($"Line was corrupted at row {row:N0}, ~{bytes:N0} bytes: '{line}'");
    }
}
