using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pgvector.Dapper;
using Rag.Application.Interfaces;
using Rag.Application.Services;
using Rag.Infrastructure.LlamaParse;
using Rag.Infrastructure.Ollama;
using Rag.Infrastructure.Persistence;

namespace Rag.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        SqlMapper.AddTypeHandler(new VectorTypeHandler());

        services.Configure<LlamaCloudOptions>(configuration.GetSection(LlamaCloudOptions.SectionName));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));
        services.Configure<ChunkingOptions>(configuration.GetSection("Chunking"));

        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing required configuration: ConnectionStrings:Postgres");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        services.AddSingleton(dataSourceBuilder.Build());

        var llamaCloudBaseUrl = configuration.GetSection(LlamaCloudOptions.SectionName)["BaseUrl"] ?? "https://api.cloud.llamaindex.ai";
        services.AddHttpClient<ILlamaParseService, LlamaParseService>(client =>
        {
            client.BaseAddress = new Uri(EnsureTrailingSlash(llamaCloudBaseUrl));
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        var ollamaBaseUrl = configuration.GetSection(OllamaOptions.SectionName)["BaseUrl"] ?? "http://localhost:11434";
        var ollamaTimeoutSeconds = configuration.GetSection(OllamaOptions.SectionName).GetValue<int?>("RequestTimeoutSeconds") ?? 120;

        services.AddHttpClient<IEmbeddingService, OllamaEmbeddingService>(client =>
        {
            client.BaseAddress = new Uri(EnsureTrailingSlash(ollamaBaseUrl));
            client.Timeout = TimeSpan.FromSeconds(ollamaTimeoutSeconds);
        });

        services.AddHttpClient<ILlmService, OllamaLlmService>(client =>
        {
            client.BaseAddress = new Uri(EnsureTrailingSlash(ollamaBaseUrl));
            client.Timeout = TimeSpan.FromSeconds(ollamaTimeoutSeconds);
        });

        services.AddScoped<IRagRepository, RagRepository>();
        services.AddScoped<ChunkingService>();
        services.AddScoped<IDocumentIngestionService, DocumentIngestionService>();
        services.AddScoped<IRagService, RagService>();

        return services;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
