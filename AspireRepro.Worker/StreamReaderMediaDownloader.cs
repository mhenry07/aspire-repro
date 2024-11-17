using System.Text;
using AspireRepro.Resource;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

/// <summary>
/// Reads and processes the response using StreamReader and MediaDownloader without using System.IO.Pipelines
/// </summary>
/// <remarks>
/// This buffers the whole response in memory before processing.
/// Also, it doesn't do extra processing work to try to trigger the issue.
/// </remarks>
public class StreamReaderMediaDownloader(
    ILogger<StreamReaderMediaDownloader> logger, IOptions<ReadOptions> options, ResourceClient resourceClient)
{
    public async Task ReadAsync(CancellationToken cancellationToken)
    {
        var bytesReceived = 0L;
        var row = 0L;
        var actual = new byte[Formatter.MaxLineLength];
        var expected = new byte[Formatter.MaxLineLength];
        string? line;

        // how do we read the stream while it's being written to?
        using var stream = new MemoryStream();
        var mediaDownloader = new MediaDownloader(resourceClient.HttpClient, options);
        await mediaDownloader.DownloadAsync("/get", stream, cancellationToken);

        stream.Position = 0;
        using var reader = new StreamReader(stream);

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            var actualLength = Encoding.UTF8.GetBytes(line, actual);
            var expectedLength = Formatter.Format(expected, row, eol: false);
            if (!actual.AsSpan(0, actualLength).SequenceEqual(expected.AsSpan(0, expectedLength)))
            {
                var expectedLine = Encoding.UTF8.GetString(expected.AsSpan(0, expectedLength));
                logger.LogError("StreamReaderMediaDownloader: Actual line did not match expected line at row {Row:N0}, ~{BytesReceived:N0} bytes\nActual:   '{ActualLine}'\nExpected: '{ExpectedLine}'", row, bytesReceived, line, expectedLine);
            }

            bytesReceived += actualLength + 1;

            row++;

            if (row % 1_000_000 == 0)
                logger.LogInformation("StreamReaderMediaDownloader: Read {Count:N0} lines, ~{BytesReceived:N0} bytes", row, bytesReceived);
        }
    }
}
