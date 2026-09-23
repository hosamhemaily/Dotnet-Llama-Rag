namespace Rag.Infrastructure.Ollama;

/// <summary>Bound from the "Ollama" configuration section.</summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Embedding model, e.g. "nomic-embed-text". Changing this changes the embedding
    /// dimension - see <see cref="EmbeddingDimension"/> and the README section on
    /// changing the embedding model.
    /// </summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Vector dimension produced by <see cref="EmbeddingModel"/>. Must match the
    /// `vector(N)` column in database/001-init.sql exactly, or inserts will fail.
    /// nomic-embed-text produces 768-dimensional vectors.
    /// </summary>
    public int EmbeddingDimension { get; set; } = 768;

    /// <summary>Chat/completion model, e.g. "qwen3:8b".</summary>
    public string ChatModel { get; set; } = "qwen3:8b";

    public double Temperature { get; set; } = 0.1;

    public int RequestTimeoutSeconds { get; set; } = 120;
}
