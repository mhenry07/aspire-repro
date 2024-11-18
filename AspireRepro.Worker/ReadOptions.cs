namespace AspireRepro.Worker;

// see notes in README.md
public class ReadOptions
{
    public required string BaseAddress { get; set; }
    public int? BatchSize { get; set; }
    public int? ChunkSize { get; set; }
    public TimeSpan? IoDelay { get; set; }
    public ReaderType ReaderType { get; set; } = ReaderType.PipeMediaDownloader;
}

public enum ReaderType
{
    PipeMediaDownloader,
    PipeCopyTo,
    ReadWriteStreamMediaDownloader,
    StreamReaderMediaDownloader,
    ResponseVerifier
}
