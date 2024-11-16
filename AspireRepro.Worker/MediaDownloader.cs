using System.Diagnostics.CodeAnalysis;

namespace AspireRepro.Worker;

// adapted from https://github.com/googleapis/google-api-dotnet-client/blob/main/Src/Support/Google.Apis/Download/MediaDownloader.cs
[SuppressMessage("Performance", "CA1835:Prefer the 'Memory'-based overloads for 'ReadAsync' and 'WriteAsync'", Justification = "Mimicking Google's implementation")]
public class MediaDownloader(HttpClient httpClient, ReadOptions options)
{
    private readonly int _chunkSize = options.ChunkSize ?? 10_485_760;

    public async Task DownloadAsync(string url, Stream stream, CancellationToken cancellationToken)
    {
        ////var chunkSize = DefaultChunkSize;
        ////var chunkSize = 1_000_000; // HttpIOException: Received an invalid end of chunk terminator
        //var chunkSize = 65_536; // Line was corrupted at row 62,062, ~5,195,289 bytes: 'abc,62031,def,01/01/0001 00:01:02 +00:00,ghi,31,jkl,01/01/0001 17:13:51 +00:00,mno'
        //// chunkSize = 65_536 reproduces the issue with Debugger attached and does not cause the issue when the debugger is not attached

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
            //var str = Encoding.UTF8.GetString(Data.AsSpan(0, Count));
            //Console.WriteLine(str);
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
