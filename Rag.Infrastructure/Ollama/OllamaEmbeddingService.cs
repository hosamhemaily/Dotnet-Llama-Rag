using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rag.Application.Exceptions;
using Rag.Application.Interfaces;

namespace Rag.Infrastructure.Ollama;

/// <summary>
/// Generates embeddings via Ollama's local REST API (POST /api/embed).
/// See https://github.com/ollama/ollama/blob/main/docs/api.md#generate-embeddings.
/// </summary>
public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingService> _logger;

    public OllamaEmbeddingService(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public int Dimension => _options.EmbeddingDimension;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var result = await EmbedBatchAsync([text], cancellationToken);
        return result[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var requestBody = new OllamaEmbedRequest(_options.EmbeddingModel, texts);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/embed", requestBody, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaUnavailableException(
                $"Could not reach Ollama at {_httpClient.BaseAddress}. Is Ollama running? ({ex.Message})", ex);
        }

        await EnsureModelAvailableAsync(response, _options.EmbeddingModel, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cancellationToken)
            ?? throw new OllamaUnavailableException("Ollama returned an empty embeddings response.");

        if (payload.Embeddings.Count != texts.Count)
        {
            throw new OllamaUnavailableException(
                $"Ollama returned {payload.Embeddings.Count} embeddings for {texts.Count} inputs.");
        }

        _logger.LogInformation("Generated {Count} embeddings using model {Model}", payload.Embeddings.Count, _options.EmbeddingModel);

        return payload.Embeddings.Select(e => e.Select(v => (float)v).ToArray()).ToList();
    }

    private static async Task EnsureModelAvailableAsync(HttpResponseMessage response, string model, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (body.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            throw new OllamaUnavailableException(
                $"Ollama model '{model}' is not installed. Run: ollama pull {model}");
        }

        throw new OllamaUnavailableException($"Ollama embedding request failed with HTTP {(int)response.StatusCode}: {body}");
    }

    private sealed record OllamaEmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record OllamaEmbedResponse(
        [property: JsonPropertyName("embeddings")] IReadOnlyList<IReadOnlyList<double>> Embeddings);
}
