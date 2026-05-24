var builder = DistributedApplication.CreateBuilder(args);

var sampleApi = builder.AddProject("sampleapi", "../RateLimiter.SampleApi/RateLimiter.SampleApi.csproj");

builder.AddProject("gateway", "../RateLimiter.Gateway/RateLimiter.Gateway.csproj")
    .WithReference(sampleApi);

builder.Build().Run();
