namespace Rag.Domain.Models;

/// <summary>A question asked against previously ingested documents.</summary>
public sealed record RagQuery
{
    public required string Question { get; init; }

    /// <summary>Number of top similar chunks to retrieve for context. Defaults to 5.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>Optional filter to restrict the search to a single document.</summary>
    public Guid? DocumentId { get; init; }
}
