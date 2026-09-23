using System.Text;
using Microsoft.Extensions.Logging;
using Rag.Application.Interfaces;
using Rag.Domain.Models;

namespace Rag.Application.Services;

/// <summary>
/// Orchestrates the query pipeline: embed question -> vector search -> build grounded
/// prompt -> LLM completion -> parse structured JSON -> reconcile sources against
/// the actually retrieved chunks (so the LLM cannot invent document names or pages).
/// </summary>
public sealed class RagService : IRagService
{
    private const string SystemPrompt = """
        You are a precise question-answering assistant. You answer questions using ONLY the
        context provided below. Follow these rules strictly:

        1. Answer ONLY using information present in the supplied context. Do not use outside knowledge.
        2. Do not invent, guess, or hallucinate facts, document names, or page numbers.
        3. If the context does not contain enough information to answer, say so explicitly in the "answer" field.
        4. Reference the sources you actually used, identified by their exact "documentName" and "pageNumber" as given in the context.
        5. Respond with ONLY valid JSON, and nothing else - no markdown code fences, no commentary before or after.

        The JSON must match this exact shape:
        {
          "answer": "your answer as plain text",
          "sources": [
            { "documentName": "exact document name from context", "pageNumber": 4 }
          ]
        }
        """;

    private readonly IEmbeddingService _embeddingService;
    private readonly IRagRepository _repository;
    private readonly ILlmService _llmService;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IEmbeddingService embeddingService,
        IRagRepository repository,
        ILlmService llmService,
        ILogger<RagService> logger)
    {
        _embeddingService = embeddingService;
        _repository = repository;
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<RagResponse> AskAsync(RagQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Question))
        {
            throw new ArgumentException("Question must not be empty.", nameof(query));
        }

        var topK = query.TopK <= 0 ? 5 : query.TopK;

        var questionEmbedding = await _embeddingService.EmbedAsync(query.Question, cancellationToken);

        var results = await _repository.SearchAsync(questionEmbedding, topK, query.DocumentId, cancellationToken);

        _logger.LogInformation("Vector search returned {Count} chunks for question", results.Count);

        if (results.Count == 0)
        {
            return new RagResponse
            {
                Answer = "No relevant information was found in the ingested documents to answer this question.",
                Sources = [],
            };
        }

        var userPrompt = BuildUserPrompt(query.Question, results);

        var rawCompletion = await _llmService.GenerateAsync(SystemPrompt, userPrompt, cancellationToken);

        _logger.LogInformation("LLM generation completed ({Length} characters)", rawCompletion.Length);

        var parsed = LlmResponseParser.Parse(rawCompletion);

        var sources = ReconcileSources(parsed.Sources, results);

        return new RagResponse
        {
            Answer = parsed.Answer,
            Sources = sources,
        };
    }

    private static string BuildUserPrompt(string question, IReadOnlyList<ChunkSearchResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Context:");
        sb.AppendLine();

        foreach (var result in results)
        {
            sb.AppendLine($"---\ndocumentName: {result.Chunk.DocumentName}\npageNumber: {(result.Chunk.PageNumber?.ToString() ?? "unknown")}\ncontent:\n{result.Chunk.Content}\n---");
            sb.AppendLine();
        }

        sb.AppendLine("Question:");
        sb.AppendLine(question);

        return sb.ToString();
    }

    /// <summary>
    /// Maps the LLM's claimed sources back onto the actually-retrieved chunks so that
    /// content and similarity always reflect ground truth, never the model's own text.
    /// Falls back to the full retrieved set if the model cited nothing we can match.
    /// </summary>
    private IReadOnlyList<RagSource> ReconcileSources(
        IReadOnlyList<LlmSourceRef> claimedSources,
        IReadOnlyList<ChunkSearchResult> retrieved)
    {
        var reconciled = new List<RagSource>();

        foreach (var claim in claimedSources)
        {
            var match = retrieved.FirstOrDefault(r =>
                string.Equals(r.Chunk.DocumentName, claim.DocumentName, StringComparison.OrdinalIgnoreCase) &&
                r.Chunk.PageNumber == claim.PageNumber);

            if (match is null)
            {
                _logger.LogWarning(
                    "LLM cited a source not present in retrieved context (documentName={DocumentName}, pageNumber={PageNumber}); discarding it",
                    claim.DocumentName, claim.PageNumber);
                continue;
            }

            reconciled.Add(ToSource(match));
        }

        if (reconciled.Count > 0)
        {
            return reconciled;
        }

        _logger.LogInformation("No LLM-cited sources matched retrieved context; falling back to all retrieved chunks");
        return retrieved.Select(ToSource).ToList();
    }

    private static RagSource ToSource(ChunkSearchResult result) => new()
    {
        DocumentName = result.Chunk.DocumentName,
        PageNumber = result.Chunk.PageNumber,
        Content = result.Chunk.Content,
        Similarity = result.Similarity,
    };
}
