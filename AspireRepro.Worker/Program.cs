using AspireRepro.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// see README.md for notes on options
var readOptions = new ReadOptions
{
    BaseAddress = new("http://resource"),
    BatchSize = 100,
    ChunkSize = 65_536,
    IoDelay = TimeSpan.FromMilliseconds(15)
};

builder.Services
    .AddSingleton<Pipeline>()
    .AddSingleton<ReadOptions>(readOptions)
    .AddHostedService<Worker>()
    .AddHttpClient<Pipeline>(client => client.BaseAddress = new(readOptions.BaseAddress));

var host = builder.Build();
host.Run();
