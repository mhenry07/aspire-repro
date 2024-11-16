# README

To reproduce the issue, run the AspireRepro.AppHost project in the Visual Studio debugger and watch the logs
from AspireRepro.Worker. With the debugger attached and a ChunkSize of 65_536, I am seeing the data discrepancy
at row 64,979, ~5,440,060 bytes on Windows 10 using Visual Studio 17.12:

```
Line was corrupted at row 64,979, ~5,440,060 bytes:
Actual:   'abc,64941,def,01/01/0001 00:01:04 +00:00,ghi,941,jkl,01/01/0001 18:02:21 +00:00,mno'
Expected: 'abc,64979,def,01/01/0001 00:01:04 +00:00,ghi,979,jkl,01/01/0001 18:02:59 +00:00,mno'
```

Compare running with the debugger attached vs. detached.

The reproduction features the following:

- HttpClient and MediaDownloader are used to download a large CSV file using the content stream (from AspireRepro.Resource)
- MediaDownloader is an adaptation of the Google Cloud Storage downloader
    - see: https://github.com/googleapis/google-api-dotnet-client/blob/main/Src/Support/Google.Apis/Download/MediaDownloader.cs
- an implementation of System.IO.Pipelines is used to process each line
- some work is done on the lines, including:
    - lines are compared to expected to check for data corruption
    - for every `BatchSize` lines, some fake I/O is performed (Task.Delay)

`ReadOptions` can be set in AspireRepro.Worker/Program.cs:

## ChunkSize

ChunkSize seems to be a significant factor in reproducing the issue. It affects MediaDownloader.CountedBuffer.Fill.
(In the real application, I experience the equivalent issue at the default 10 MiB.)

Tested ChunkSize values:

- 10_485_760: 10 MiB is the Google default, but in this repo it results in: HttpIOException: The response ended prematurely.
- 1_000_000: in this repo 1 MB results in: HttpIOException: Received an invalid end of chunk terminator
- 65_536: 64 KiB reproduces the issue only when the debugger is attached, which matches the behavior in real code
    - in real code, I experienced this with default chunk size of 10 MiB

## BaseAddress

The HttpClient base address.

- `"https+http://resource"`: https results in: Win32Exception (0x80090330): The specified data could not be decrypted.
- `"http://resource"`: http seems to work better than https in this repo

## BatchSize

- mimics processing lines according to BatchSize

## IoDelay

- mimics doing some I/O when processing a batch of lines
