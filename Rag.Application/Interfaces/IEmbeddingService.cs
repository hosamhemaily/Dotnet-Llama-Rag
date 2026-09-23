namespace Rag.Application.Interfaces;

/// <summary>Generates text embeddings using the configured local Ollama embedding model.</summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);

    /// <summary>The vector dimension produced by the configured embedding model.</summary>
    int Dimension { get; }
}
