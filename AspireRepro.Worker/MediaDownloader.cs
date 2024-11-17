using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

// Modified from: https://github.com/googleapis/google-api-dotnet-client/blob/main/Src/Support/Google.Apis/Download/MediaDownloader.cs
// For license, see MediaDownloader.LICENSE.txt.
// Simplified implementation in order to reproduce issue.

// Apart from adjusting ChunkSize, we don't have much control of this implementation when going through the
// [Google.Cloud.Storage.V1](https://www.nuget.org/packages/Google.Cloud.Storage.V1) client library.
[SuppressMessage("Performance", "CA1835:Prefer the 'Memory'-based overloads for 'ReadAsync' and 'WriteAsync'", Justification = "Mimicking Google's implementation")]
public class MediaDownloader(HttpClient httpClient, IOptions<ReadOptions> options)
{
    // see README.md for chunk size notes
    private readonly int _chunkSize = options.Value.ChunkSize ?? 10_485_760;

    public async Task DownloadAsync(string url, Stream stream, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new CountedBuffer(_chunkSize + 1);
        while (true)
        {
            await buffer.Fill(responseStream, cancellationToken).ConfigureAwait(false);
            var bytesToReturn = Math.Min(_chunkSize, buffer.Count);
            await stream.WriteAsync(buffer.Data, 0, bytesToReturn, cancellationToken).ConfigureAwait(false);
            buffer.RemoveFromFront(_chunkSize);
            if (buffer.IsEmpty)
                break;
        }
    }

    private class CountedBuffer(int size)
    {
        public byte[] Data { get; set; } = new byte[size];
        public int Count { get; private set; } = 0;
        public bool IsEmpty => Count == 0;

        public async Task Fill(Stream stream, CancellationToken cancellationToken)
        {
            while (Count < Data.Length)
            {
                var num = await stream.ReadAsync(Data, Count, Data.Length - Count, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
                if (num != 0)
                {
                    Count += num;
                    continue;
                }
                break;
            }
        }

        public void RemoveFromFront(int n)
        {
            if (n >= Count)
            {
                Count = 0;
                return;
            }
            Array.Copy(Data, n, Data, 0, Count - n);
            Count -= n;
        }
    }
}
