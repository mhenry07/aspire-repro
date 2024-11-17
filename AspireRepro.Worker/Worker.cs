using Microsoft.Extensions.Options;

namespace AspireRepro.Worker;

public class Worker(IOptions<ReadOptions> options, Pipeline pipeline, ResponseVerifier verifier) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1_000, stoppingToken);

        var pipelineTask = pipeline.ReadAsync(stoppingToken);
        var verifierTask = options.Value.ExecuteResponseVerifier
            ? Task.Run(() => verifier.VerifyAsync(stoppingToken), stoppingToken)
            : Task.CompletedTask;

        await Task.WhenAll(pipelineTask, verifierTask);
    }
}
