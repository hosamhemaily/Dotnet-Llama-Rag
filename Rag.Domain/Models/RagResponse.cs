namespace Rag.Domain.Models;

/// <summary>Structured answer returned to the caller, grounded in retrieved sources.</summary>
public sealed record RagResponse
{
    public required string Answer { get; init; }

    public required IReadOnlyList<RagSource> Sources { get; init; }
}

/// <summary>One piece of retrieved context that backs the answer.</summary>
public sealed record RagSource
{
    public required string DocumentName { get; init; }

    public int? PageNumber { get; init; }

    public required string Content { get; init; }

    /// <summary>Cosine similarity of this chunk to the query, in the range [-1, 1] (higher is more similar).</summary>
    public required double Similarity { get; init; }
}
