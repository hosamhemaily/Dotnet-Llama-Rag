namespace Rag.Domain.Entities;

/// <summary>
/// A single retrievable unit of a parsed document: one chunk of text plus the
/// metadata needed to trace it back to its exact location in the source document.
/// </summary>
public sealed record DocumentChunk
{
    public required Guid Id { get; init; }

    public required Guid DocumentId { get; init; }

    public required string DocumentName { get; init; }

    public required int ChunkIndex { get; init; }

    public int? PageNumber { get; init; }

    public string? SectionTitle { get; init; }

    public required string Content { get; init; }

    /// <summary>
    /// The embedding vector produced by the configured Ollama embedding model.
    /// Dimensionality must match the <c>vector(N)</c> column defined in the database schema.
    /// </summary>
    public required float[] Embedding { get; init; }

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
