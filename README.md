# README

Reported issue: [Visual Studio 17.12 Debugger corrupts System.IO.Pipelines data](https://developercommunity.visualstudio.com/t/Visual-Studio-1712-Debugger-corrupts-Sy/10789416)

To reproduce the issue, run the AspireRepro.AppHost project in the Visual Studio debugger and watch the logs
from AspireRepro.Worker.

Compare running with the debugger attached vs. detached.

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

`ReadOptions` can be set in AspireRepro.Worker/Program.cs:

## ChunkSize

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

(note: after some tweaks, the ChunkSize results changed from a previous iteration)

## BaseAddress

The HttpClient base address.

- `https+http://resource`: https results in: Win32Exception (0x80090330): The specified data could not be decrypted.
- `http://resource`: http seems to work better than https in this repo

## BatchSize

- mimics processing lines according to BatchSize

## IoDelay

- mimics doing some I/O when processing a batch of lines
