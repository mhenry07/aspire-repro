namespace AspireRepro.Worker;

// see notes in README.md
public class ReadOptions
{
    public required string BaseAddress { get; set; }
    public int? BatchSize { get; set; }
    public int? ChunkSize { get; set; }
    public bool ExecuteResponseVerifier { get; set; }
    public TimeSpan? IoDelay { get; set; }
}
