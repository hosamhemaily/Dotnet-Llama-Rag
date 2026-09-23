namespace Rag.Application.Interfaces;

/// <summary>Generates chat completions using the configured local Ollama chat model.</summary>
public interface ILlmService
{
    /// <summary>
    /// Sends a system + user prompt pair to the LLM and returns the raw text response.
    /// </summary>
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
