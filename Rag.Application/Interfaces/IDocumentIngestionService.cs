namespace Rag.Application.Interfaces;

/// <summary>Result of ingesting one document: parse -> chunk -> embed -> persist.</summary>
public sealed record DocumentIngestionResult(Guid DocumentId, string DocumentName, int ChunkCount, int PageCount);

/// <summary>Orchestrates the full ingestion pipeline for an uploaded document.</summary>
public interface IDocumentIngestionService
{
    Task<DocumentIngestionResult> IngestAsync(Stream fileStream, string fileName, CancellationToken cancellationToken);
}
