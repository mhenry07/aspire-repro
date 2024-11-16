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
        options.ChunkSize = 1_000_000;
        options.IoDelay = TimeSpan.FromMilliseconds(15);
    })
    .AddSingleton<Pipeline>()
    .AddHostedService<Worker>()
    .AddHttpClient<Pipeline>((serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<ReadOptions>>();
        client.BaseAddress = new(options.Value.BaseAddress);
    });

var host = builder.Build();
host.Run();
