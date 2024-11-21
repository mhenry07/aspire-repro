namespace AspireRepro.Worker;

/// <remarks>See notes in README.md</remarks>
public class ReadOptions
{
    public required string BaseAddress { get; set; }
    public int? BatchSize { get; set; }
    public int? ChunkSize { get; set; }
    public TimeSpan? IoDelay { get; set; }
    public ReaderType ReaderType { get; set; } = ReaderType.PipeFillBuffer;
}

public enum ReaderType
{
    BufferReader,
    BufferReferenceStream,
    FillBufferReferenceStream,

    /// <remarks>Triggers the issue</remarks>
    FillBufferReader,

    PipeBuffer,
    PipeCopyTo,

    /// <remarks>Triggers the issue</remarks>
    PipeFillBuffer,

    /// <remarks>Triggers the issue</remarks>
    PipeMediaDownloader,

    /// <remarks>Triggers the issue</remarks>
    PipeMediaDownloaderSemaphoreStream,

    ReadWriteStreamMediaDownloader,
    StreamReaderMediaDownloader,
    ResponseVerifier
}
