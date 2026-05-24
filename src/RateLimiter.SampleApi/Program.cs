var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi(options =>
{
    // Remove server URLs so Swagger UI uses the current host (works through Gateway proxy)
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Servers?.Clear();
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Serve OpenAPI doc at standard path and under /api/ prefix for Gateway proxy access
app.MapOpenApi();
app.MapOpenApi("/api/openapi/{documentName}.json");

// Swagger UI (development only)
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "SampleApi");
    });
}

app.MapGet("/api/products", () => Results.Ok(new[]
{
    new { Id = 1, Name = "Widget", Price = 9.99 },
    new { Id = 2, Name = "Gadget", Price = 19.99 },
    new { Id = 3, Name = "Doohickey", Price = 4.99 }
}));

app.Run();
