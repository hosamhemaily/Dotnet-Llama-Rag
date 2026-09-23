using Rag.Domain.Models;

namespace Rag.Application.Interfaces;

/// <summary>Orchestrates document ingestion and question answering over ingested documents.</summary>
public interface IRagService
{
    Task<RagResponse> AskAsync(RagQuery query, CancellationToken cancellationToken);
}
