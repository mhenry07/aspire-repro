using AspireRepro.Worker;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services
    .Configure<ReadOptions>(static options =>
    {
        // see README.md for notes on options
        options.BaseAddress = "http://resource";
        options.BatchSize = 100;
        options.ChunkSize = 4_000_000;
        options.IoDelay = TimeSpan.FromMilliseconds(15);
        options.ReaderType = ReaderType.PipeMediaDownloader;
    })
    .AddSingleton<PipeCopyTo>()
    .AddSingleton<PipeMediaDownloader>()
    .AddSingleton<ResponseVerifier>()
    .AddSingleton<ReadWriteStreamMediaDownloader>()
    .AddSingleton<StreamReaderMediaDownloader>()
    .AddHostedService<Worker>();

builder.Services.AddHttpClient<ResourceClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ReadOptions>>();
    client.BaseAddress = new(options.Value.BaseAddress);
});

var host = builder.Build();
host.Run();
