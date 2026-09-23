using Microsoft.Extensions.Logging.Abstractions;
using Rag.Application.Interfaces;
using Rag.Application.Services;
using Rag.Domain.Entities;
using Rag.Domain.Models;

namespace Rag.Tests;

public class RagServiceTests
{
    private static DocumentChunk MakeChunk(string doc, int page, string content) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        DocumentName = doc,
        ChunkIndex = 0,
        PageNumber = page,
        SectionTitle = null,
        Content = content,
        Embedding = [0.1f, 0.2f],
    };

    [Fact]
    public async Task AskAsync_NoRetrievedChunks_ReturnsNotAvailableWithoutCallingLlm()
    {
        var embedding = new FakeEmbeddingService();
        var repository = new FakeRagRepository([]);
        var llm = new FakeLlmService("should not be called");

        var service = new RagService(embedding, repository, llm, NullLogger<RagService>.Instance);

        var response = await service.AskAsync(new RagQuery { Question = "What is this?" }, CancellationToken.None);

        Assert.Empty(response.Sources);
        Assert.Contains("No relevant information", response.Answer);
        Assert.Equal(0, llm.CallCount);
    }

    [Fact]
    public async Task AskAsync_ValidLlmCitation_ReconciledAgainstRetrievedChunk()
    {
        var chunk = MakeChunk("manual.pdf", 4, "Install by unpacking the box.");
        var repository = new FakeRagRepository([new ChunkSearchResult(chunk, 0.87)]);
        var llm = new FakeLlmService("""{ "answer": "Unpack the box.", "sources": [ { "documentName": "manual.pdf", "pageNumber": 4 } ] }""");

        var service = new RagService(new FakeEmbeddingService(), repository, llm, NullLogger<RagService>.Instance);

        var response = await service.AskAsync(new RagQuery { Question = "How do I install it?" }, CancellationToken.None);

        Assert.Equal("Unpack the box.", response.Answer);
        var source = Assert.Single(response.Sources);
        Assert.Equal("manual.pdf", source.DocumentName);
        Assert.Equal(4, source.PageNumber);
        Assert.Equal(0.87, source.Similarity);
        Assert.Equal("Install by unpacking the box.", source.Content);
    }

    [Fact]
    public async Task AskAsync_LlmCitesUnknownSource_FallsBackToAllRetrievedChunks()
    {
        var chunk = MakeChunk("manual.pdf", 4, "Install by unpacking the box.");
        var repository = new FakeRagRepository([new ChunkSearchResult(chunk, 0.87)]);
        var llm = new FakeLlmService("""{ "answer": "Unpack the box.", "sources": [ { "documentName": "made-up.pdf", "pageNumber": 99 } ] }""");

        var service = new RagService(new FakeEmbeddingService(), repository, llm, NullLogger<RagService>.Instance);

        var response = await service.AskAsync(new RagQuery { Question = "How do I install it?" }, CancellationToken.None);

        var source = Assert.Single(response.Sources);
        Assert.Equal("manual.pdf", source.DocumentName);
    }

    [Fact]
    public async Task AskAsync_EmptyQuestion_Throws()
    {
        var service = new RagService(new FakeEmbeddingService(), new FakeRagRepository([]), new FakeLlmService(""), NullLogger<RagService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AskAsync(new RagQuery { Question = "  " }, CancellationToken.None));
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public int Dimension => 2;

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken) => Task.FromResult(new[] { 0.1f, 0.2f });

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => new[] { 0.1f, 0.2f }).ToList());
    }

    private sealed class FakeRagRepository : IRagRepository
    {
        private readonly IReadOnlyList<ChunkSearchResult> _results;

        public FakeRagRepository(IReadOnlyList<ChunkSearchResult> results) => _results = results;

        public Task InsertChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(float[] queryEmbedding, int topK, Guid? documentId, CancellationToken cancellationToken) =>
            Task.FromResult(_results);
    }

    private sealed class FakeLlmService : ILlmService
    {
        private readonly string _response;

        public FakeLlmService(string response) => _response = response;

        public int CallCount { get; private set; }

        public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }
}
