using Microsoft.OpenApi;
using Rag.Api;
using Rag.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Bridge the documented LLAMACLOUD_API_KEY env var onto configuration, in addition to the
// standard ASP.NET Core convention (LlamaCloud__ApiKey), for a friendlier local setup.
var llamaCloudApiKeyFromEnv = Environment.GetEnvironmentVariable("LLAMACLOUD_API_KEY");
if (!string.IsNullOrWhiteSpace(llamaCloudApiKeyFromEnv))
{
    builder.Configuration["LlamaCloud:ApiKey"] = llamaCloudApiKeyFromEnv;
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DotnetLlamaRag API",
        Version = "v1",
        Description = "Local RAG API: LlamaParse ingestion + Ollama embeddings/LLM + PostgreSQL/pgvector retrieval.",
    });
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "DotnetLlamaRag API v1");
});

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();
