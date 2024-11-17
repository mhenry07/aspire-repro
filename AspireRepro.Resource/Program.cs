using AspireRepro.Resource;

const int MaxRows = 1_000_000_000;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var app = builder.Build();

app.MapDefaultEndpoints();

// splitting the line into multiple writes appears to affect how the lines get corrupted when the pipeline processes them
app.MapGet("/get", async context =>
{
    var stopping = app.Lifetime.ApplicationStopping;
    context.Response.ContentType = "text/csv;charset=utf-8";
    await context.Response.StartAsync(stopping);

    var line = new byte[Formatter.MaxLineLength];
    var row = 0L;
    using var stream = context.Response.Body;
    while (row < MaxRows)
    {
        var length = Formatter.Format(line, row, eol: true);
        var split = (int)(row % (length / 2)) + 1;
        await stream.WriteAsync(line.AsMemory(0, split), stopping);
        await stream.WriteAsync(line.AsMemory(split, length - split), stopping);
        row++;
    }

    await stream.FlushAsync(stopping);
    await context.Response.CompleteAsync();
});

app.Run();
