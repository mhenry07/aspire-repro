using AspireRepro.Resource;

const int MaxRows = 1_000_000_000;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/get", async context =>
{
    var stopping = app.Lifetime.ApplicationStopping;
    context.Response.ContentType = "text/csv; charset=utf8";
    await context.Response.StartAsync(stopping);

    var line = new byte[ValueUtility.MaxLineLength];
    var row = 0;
    using var stream = context.Response.Body;
    while (row < MaxRows)
    {
        var length = ValueUtility.Format(line, row, eol: true);
        await stream.WriteAsync(line.AsMemory(0, length), stopping);
        row++;
    }

    await stream.FlushAsync(stopping);
    await context.Response.CompleteAsync();
});

app.Run();
