using System.Text;
using AspireRepro.Resource;

namespace AspireRepro.Worker;

/// <summary>
/// Reads the response without using MediaDownloader or System.IO.Pipelines and verifies that the response contains the
/// expected lines
/// </summary>
public class ResponseVerifier(ILogger<ResponseVerifier> logger, ResourceClient resourceClient)
{
    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        var bytesReceived = 0L;
        var row = 0L;
        var actual = new byte[Formatter.MaxLineLength];
        var expected = new byte[Formatter.MaxLineLength];
        string? line;
        var response = await resourceClient.HttpClient.GetAsync("/get", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            var actualLength = Encoding.UTF8.GetBytes(line, actual);
            var expectedLength = Formatter.Format(expected, row, eol: false);
            if (!actual.AsSpan(0, actualLength).SequenceEqual(expected.AsSpan(0, expectedLength)))
            {
                var expectedLine = Encoding.UTF8.GetString(expected.AsSpan(0, expectedLength));
                logger.LogError("ResponseVerifier: Actual line did not match expected line at row {Row:N0}, ~{BytesReceived:N0} bytes\nActual:   '{ActualLine}'\nExpected: '{ExpectedLine}'", row, bytesReceived, line, expectedLine);
            }

            bytesReceived += actualLength + 1;

            row++;

            if (row % 1_000_000 == 0)
                logger.LogInformation("ResponseVerifier: Verified {Count:N0} lines, ~{BytesReceived:N0} bytes", row, bytesReceived);
        }
    }
}
