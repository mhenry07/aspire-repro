using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

public class Worker(
    ILogger<Worker> logger,
    IOptions<ReadOptions> options,
    BufferReader bufferReader,
    BufferReferenceStream bufferReferenceStream,
    PipeBuffer pipeBuffer,
    PipeCopyTo pipeCopyTo,
    PipeMediaDownloader pipeMediaDownloader,
    ReadWriteStreamMediaDownloader readWriteStreamMediaDownloader,
    StreamReaderMediaDownloader streamReaderMediaDownloader,
    ResponseVerifier verifier)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1_000, stoppingToken);

        logger.LogInformation("Starting {ReaderType}", options.Value.ReaderType);
        var stopwatch = Stopwatch.StartNew();
        await (options.Value.ReaderType switch
        {
            ReaderType.BufferReader => bufferReader.ReadAsync(stoppingToken),
            ReaderType.BufferReferenceStream => bufferReferenceStream.ReadAsync(stoppingToken),
            ReaderType.FillBufferReader => bufferReader.ReadAsync(stoppingToken),
            ReaderType.FillBufferReferenceStream => bufferReferenceStream.ReadAsync(stoppingToken),
            ReaderType.PipeBuffer => pipeBuffer.ReadAsync(stoppingToken),
            ReaderType.PipeCopyTo => pipeCopyTo.ReadAsync(stoppingToken),
            ReaderType.PipeFillBuffer => pipeBuffer.ReadAsync(stoppingToken),
            ReaderType.PipeMediaDownloader => pipeMediaDownloader.ReadAsync(stoppingToken),
            ReaderType.PipeMediaDownloaderSemaphoreStream => pipeMediaDownloader.ReadAsync(stoppingToken),
            ReaderType.ReadWriteStreamMediaDownloader => readWriteStreamMediaDownloader.ReadAsync(stoppingToken),
            ReaderType.StreamReaderMediaDownloader => streamReaderMediaDownloader.ReadAsync(stoppingToken),
            ReaderType.ResponseVerifier => verifier.VerifyAsync(stoppingToken),
            _ => throw new InvalidOperationException($"Unexpected ReaderType: {options.Value.ReaderType}")
        });
        logger.LogInformation("Completed {ReaderType} in {Elapsed}", options.Value.ReaderType, stopwatch.Elapsed);
    }
}
