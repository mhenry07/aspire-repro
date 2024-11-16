var builder = DistributedApplication.CreateBuilder(args);

var resource = builder.AddProject<Projects.AspireRepro_Resource>("resource")
    .WithExternalHttpEndpoints();

var worker = builder.AddProject<Projects.AspireRepro_Worker>("worker")
    .WithReference(resource);

builder.Build().Run();
