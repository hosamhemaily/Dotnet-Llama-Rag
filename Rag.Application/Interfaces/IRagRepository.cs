using Rag.Domain.Entities;

namespace Rag.Application.Interfaces;

/// <summary>Persistence operations for document chunks and vector similarity search over pgvector.</summary>
public interface IRagRepository
{
    Task InsertChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the top <paramref name="topK"/> chunks ranked by cosine similarity to <paramref name="queryEmbedding"/>,
    /// optionally restricted to a single document.
    /// </summary>
    Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        Guid? documentId,
        CancellationToken cancellationToken);
}

/// <summary>A chunk returned from vector similarity search, with its similarity score.</summary>
public sealed record ChunkSearchResult(DocumentChunk Chunk, double Similarity);
