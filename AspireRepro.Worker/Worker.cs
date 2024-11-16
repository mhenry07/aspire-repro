namespace AspireRepro.Worker;

public class Worker(Pipeline pipeline) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1_000, stoppingToken);

        await pipeline.ReadAsync(stoppingToken);
    }
}
