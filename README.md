# README

Reported issue: [Visual Studio 17.12 Debugger corrupts System.IO.Pipelines data](https://developercommunity.visualstudio.com/t/Visual-Studio-1712-Debugger-corrupts-Sy/10789416)

To reproduce the issue, run the AspireRepro.AppHost project in the Visual Studio debugger and watch the logs
from AspireRepro.Worker.

Compare running with the debugger attached vs. detached.

Note: Since filing the issue, I've been able to reproduce the issue in VS 17.11 and with the debugger detached.

Here are some logs of when the data discrepancy occurred:

- ChunkSize: 1_000_000, debugger attached:

    ```
    Line was corrupted at row 118,096, ~10,281,405 bytes:
    Actual:   'abc,118096,def,aaaaghi,01/01/0001 00:01:58 +00:00,jkl,01/02/0001 08:48:11 +00:00,mno'
    Expected: 'abc,118096,def,aaaa,ghi,01/01/0001 00:01:58 +00:00,jkl,01/02/0001 08:48:16 +00:00,mno'
    ```

- ChunkSize: 1_000_000, debugger attached:

    ```
    Line was corrupted at row 102,958, ~8,949,250 bytes:
    Actual:   ',ghi,01/01/0001 00:01:42 +00:00,jkl,01/02/0001 04:30:29 +00:00,mno'
    Expected: 'abc,102958,def,aaaaaaaaaaa,ghi,01/01/0001 00:01:42 +00:00,jkl,01/02/0001 04:35:58 +00:00,mno'
    ```

- ChunkSize: 4_000_000, debugger attached:

    ```
    Line was corrupted at row 73,173, ~6,428,187 bytes:
    Actual:   'abc,abc,73169,def,aaaaaa,ghi,01/01/0001 00:01:13 +00:00,jkl,01/01/0001 20:19:29 +00:00,mno'
    Expected: 'abc,73173,def,aaaaaaaaaa,ghi,01/01/0001 00:01:13 +00:00,jkl,01/01/0001 20:19:33 +00:00,mno'
    ```

The reproduction features the following:

- HttpClient and MediaDownloader are used to download a large CSV file using the content stream (from AspireRepro.Resource)
- MediaDownloader is an adaptation of the Google Cloud Storage downloader
    - see: https://github.com/googleapis/google-api-dotnet-client/blob/main/Src/Support/Google.Apis/Download/MediaDownloader.cs
- an implementation of System.IO.Pipelines is used to process each line
- some work is done on the lines, including:
    - lines are compared to expected to check for data corruption
    - for every `BatchSize` lines, some fake I/O is performed (Task.WhenAll / Task.Delay)
- AspireRepro.Resource mimics a simplified version of Google Cloud Storage
    - Similar data corruption is experienced against [fake-gcs-server](https://github.com/fsouza/fake-gcs-server) when
      consumed via the [Google.Cloud.Storage.V1](https://www.nuget.org/packages/Google.Cloud.Storage.V1) client library.

## Options

`ReadOptions` can be set in AspireRepro.Worker/Program.cs:

### ChunkSize

ChunkSize seems to be a significant factor in reproducing the issue. It affects MediaDownloader.CountedBuffer.Fill.
(In the real application, I experience the equivalent issue at the default 10 MiB.)

Tested ChunkSize values:

- 10_485_760: 10 MiB is the Google default, but in this repo it results in: HttpIOException: The response ended prematurely.
- 4_000_000: Triggered the issue both with the debugger attached and detached, so it's slightly different behavior from
  the reported issue.
- 1_000_000: Reproduced the issue with the debugger attached at row 118,096, ~10,281,405 bytes / row 102,958, ~8,949,250 bytes.
    - However, with the debugger detached, I get: HttpIOException: Received an invalid chunk terminator
      after row 342,171, ~29,999,927 bytes. In my original solution and some iterations of this repro, I get no errors
      with the debugger detached.

(Note that ChunkSize results changed between v1 and v3)

### BaseAddress

The HttpClient base address.

- `https+http://resource`: https results in: Win32Exception (0x80090330): The specified data could not be decrypted.
- `http://resource`: http seems to work better than https in this repo

### BatchSize

Mimics processing lines according to BatchSize.

### IoDelay

Mimics doing some I/O when processing a batch of lines.

### ReaderType

There are multiple reader implementations. *PipeMediaDownloader*, *PipeMediaDownloaderSemaphoreStream*, and
*PipeFillBuffer* reproduce the issue and the others are for comparison and validation.

- `PipeMediaDownloader`: Reads and processes the response using System.IO.Pipelines and MediaDownloader and triggers
  the issue in some cases
- `PipeMediaDownloaderSemaphoreStream`: Uses PipeMediaDownloader and wraps the writer stream in a SemaphoreStream and
  triggers the issue in some cases
- `PipeBuffer`: Reads and processes the response using System.IO.Pipelines and a buffer without using MediaDownloader
- `PipeFillBuffer`: Uses PipeBuffer with an algorithm similar to MediaDownloader to fill the buffer and triggers the
  issue in some cases
- `PipeCopyTo`: Reads and processes the response using System.IO.Pipelines and CopyTo without using MediaDownloader
- `ReadWriteStreamMediaDownloader`: Reads and processes the response using ReadWriteStream, StreamReader, and
  MediaDownloader. It has line logic comparable to PipeMediaDownloader but does not appear to trigger the reported
  issue.
- `StreamReaderMediaDownloader`: Reads and processes the response using StreamReader and MediaDownloader without using
  System.IO.Pipelines. Uses a lot of memory since it buffers the full response.
- `ResponseVerifier`: Reads the response without using MediaDownloader or System.IO.Pipelines and verifies that the
  response contains the expected lines

## MediaDownloader notes

MediaDownloader is adapted from
[MediaDownloader.cs](https://github.com/googleapis/google-api-dotnet-client/blob/main/Src/Support/Google.Apis/Download/MediaDownloader.cs)
and is used by the [Google.Cloud.Storage.V1](https://www.nuget.org/packages/Google.Cloud.Storage.V1) client library.

- MediaDownloader.cs license: see AspireRepro.Worker/MediaDownloader.LICENSE.txt

The issue appears to occur specifically when System.IO.Pipelines and MediaDownloader or a similar algorithm are used
together (PipeMediaDownloader, PipeMediaDownloaderSemaphoreStream, and PipeFillBuffer). The other implementations don't
appear to trigger the reported issue.

Ideally, it would be preferable to get System.IO.Pipelines and MediaDownloader to work well together, so that the
Google.Cloud.Storage.V1 client library can be used to handle auth and other concerns.
