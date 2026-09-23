using Microsoft.Extensions.Logging;
using Rag.Application.Interfaces;
using Rag.Domain.Entities;

namespace Rag.Application.Services;

/// <summary>
/// Orchestrates the ingestion pipeline: parse (LlamaParse) -> chunk -> embed (Ollama) -> persist (pgvector).
/// </summary>
public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private readonly ILlamaParseService _parseService;
    private readonly ChunkingService _chunkingService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IRagRepository _repository;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        ILlamaParseService parseService,
        ChunkingService chunkingService,
        IEmbeddingService embeddingService,
        IRagRepository repository,
        ILogger<DocumentIngestionService> logger)
    {
        _parseService = parseService;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _repository = repository;
        _logger = logger;
    }

    public async Task<DocumentIngestionResult> IngestAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
    {
        if (fileStream.Length == 0)
        {
            throw new InvalidOperationException($"Uploaded file '{fileName}' is empty.");
        }

        _logger.LogInformation("Starting ingestion for {FileName}", fileName);

        var parsed = await _parseService.ParseAsync(fileStream, fileName, cancellationToken);

        if (string.IsNullOrWhiteSpace(parsed.Markdown))
        {
            throw new InvalidOperationException(
                $"LlamaParse returned no content for '{fileName}'. The document may be empty, scanned without OCR support, or unsupported.");
        }

        _logger.LogInformation(
            "LlamaParse completed for {FileName}: {PageCount} pages, {CharCount} characters",
            fileName, parsed.Pages.Count, parsed.Markdown.Length);

        var documentId = Guid.NewGuid();
        var chunks = _chunkingService.Chunk(parsed, documentId);

        if (chunks.Count == 0)
        {
            throw new InvalidOperationException($"Document '{fileName}' produced no chunks after parsing.");
        }

        var embeddings = await _embeddingService.EmbedBatchAsync(
            chunks.Select(c => c.Content).ToList(),
            cancellationToken);

        if (embeddings.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Embedding count ({embeddings.Count}) does not match chunk count ({chunks.Count}).");
        }

        var embeddedChunks = new List<DocumentChunk>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var embedding = embeddings[i];
            if (embedding.Length != _embeddingService.Dimension)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch: expected {_embeddingService.Dimension}, got {embedding.Length}. " +
                    "Check that Ollama:EmbeddingModel matches the vector column dimension in the database schema.");
            }

            embeddedChunks.Add(chunks[i] with { Embedding = embedding });
        }

        _logger.LogInformation("Generated {Count} embeddings (dimension {Dimension}) for {FileName}", embeddedChunks.Count, _embeddingService.Dimension, fileName);

        await _repository.InsertChunksAsync(embeddedChunks, cancellationToken);

        _logger.LogInformation("Persisted {Count} chunks for {FileName} (documentId {DocumentId})", embeddedChunks.Count, fileName, documentId);

        return new DocumentIngestionResult(documentId, fileName, embeddedChunks.Count, parsed.Pages.Count);
    }
}
