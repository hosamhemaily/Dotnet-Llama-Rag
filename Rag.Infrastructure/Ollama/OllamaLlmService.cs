using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rag.Application.Exceptions;
using Rag.Application.Interfaces;

namespace Rag.Infrastructure.Ollama;

/// <summary>
/// Generates chat completions via Ollama's local REST API (POST /api/chat).
/// See https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-chat-completion.
/// </summary>
public sealed class OllamaLlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaLlmService> _logger;

    /// <summary>
    /// JSON Schema handed to Ollama's "format" field so the model's decoding is
    /// grammar-constrained to this exact shape, instead of relying on the system prompt
    /// alone (small models frequently ignore "respond with only JSON").
    /// See https://github.com/ollama/ollama/blob/main/docs/api.md#structured-outputs.
    /// </summary>
    private static readonly JsonDocument AnswerFormatSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "answer": { "type": "string" },
            "sources": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "documentName": { "type": "string" },
                  "pageNumber": { "type": ["integer", "null"] }
                },
                "required": ["documentName"]
              }
            }
          },
          "required": ["answer", "sources"]
        }
        """);

    public OllamaLlmService(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaLlmService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var messages = new List<OllamaChatMessage>
        {
            new("system", systemPrompt),
            new("user", userPrompt),
        };

        var requestBody = new OllamaChatRequest(
            _options.ChatModel,
            messages,
            false,
            new OllamaChatOptions(_options.Temperature),
            Format: AnswerFormatSchema.RootElement);

        _logger.LogInformation("Sending chat completion request to Ollama using model {Model}", _options.ChatModel);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/chat", requestBody, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaUnavailableException(
                $"Could not reach Ollama at {_httpClient.BaseAddress}. Is Ollama running? ({ex.Message})", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (body.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new OllamaUnavailableException(
                    $"Ollama chat model '{_options.ChatModel}' is not installed. Run: ollama pull {_options.ChatModel}");
            }

            throw new OllamaUnavailableException($"Ollama chat request failed with HTTP {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken)
            ?? throw new OllamaUnavailableException("Ollama returned an empty chat response.");

        _logger.LogInformation("Ollama chat completion finished using model {Model}", _options.ChatModel);

        return payload.Message.Content;
    }

    private sealed record OllamaChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OllamaChatOptions(
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed record OllamaChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<OllamaChatMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaChatOptions Options,
        [property: JsonPropertyName("think")] bool Think = false,
        [property: JsonPropertyName("format")] JsonElement? Format = null);

    private sealed record OllamaChatResponse(
        [property: JsonPropertyName("message")] OllamaChatMessage Message,
        [property: JsonPropertyName("done")] bool Done);
}
