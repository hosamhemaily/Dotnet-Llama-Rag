using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;
using Rag.Application.Exceptions;
using Rag.Application.Interfaces;
using Rag.Domain.Entities;

namespace Rag.Infrastructure.Persistence;

/// <summary>
/// Dapper-based persistence for document chunks and pgvector similarity search.
/// Cosine distance (the `&lt;=&gt;` operator, backed by a `vector_cosine_ops` index) is
/// used throughout; similarity is reported as `1 - cosine_distance`.
/// </summary>
public sealed class RagRepository : IRagRepository
{
    private const string InsertSql = """
        INSERT INTO document_chunks
            (id, document_id, document_name, chunk_index, page_number, section_title, content, embedding, created_at_utc)
        VALUES
            (@Id, @DocumentId, @DocumentName, @ChunkIndex, @PageNumber, @SectionTitle, @Content, @Embedding, @CreatedAtUtc)
        """;

    private const string SearchSqlAllDocuments = """
        SELECT id AS Id,
               document_id AS DocumentId,
               document_name AS DocumentName,
               chunk_index AS ChunkIndex,
               page_number AS PageNumber,
               section_title AS SectionTitle,
               content AS Content,
               created_at_utc AS CreatedAtUtc,
               1 - (embedding <=> @QueryEmbedding) AS Similarity
        FROM document_chunks
        ORDER BY embedding <=> @QueryEmbedding
        LIMIT @TopK
        """;

    private const string SearchSqlSingleDocument = """
        SELECT id AS Id,
               document_id AS DocumentId,
               document_name AS DocumentName,
               chunk_index AS ChunkIndex,
               page_number AS PageNumber,
               section_title AS SectionTitle,
               content AS Content,
               created_at_utc AS CreatedAtUtc,
               1 - (embedding <=> @QueryEmbedding) AS Similarity
        FROM document_chunks
        WHERE document_id = @DocumentId
        ORDER BY embedding <=> @QueryEmbedding
        LIMIT @TopK
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<RagRepository> _logger;

    public RagRepository(NpgsqlDataSource dataSource, ILogger<RagRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task InsertChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        var parameters = chunks.Select(c => new
        {
            c.Id,
            c.DocumentId,
            c.DocumentName,
            c.ChunkIndex,
            c.PageNumber,
            c.SectionTitle,
            c.Content,
            Embedding = new Vector(c.Embedding),
            c.CreatedAtUtc,
        });

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var command = new CommandDefinition(InsertSql, parameters, transaction, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            throw new RagStorageException($"Failed to insert {chunks.Count} chunk(s) into PostgreSQL: {ex.Message}", ex);
        }

        _logger.LogInformation("Inserted {Count} chunks into document_chunks", chunks.Count);
    }

    public async Task<IReadOnlyList<ChunkSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        Guid? documentId,
        CancellationToken cancellationToken)
    {
        var vector = new Vector(queryEmbedding);

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            var rows = documentId is { } id
                ? await connection.QueryAsync<ChunkRow>(
                    new CommandDefinition(
                        SearchSqlSingleDocument,
                        new { QueryEmbedding = vector, TopK = topK, DocumentId = id },
                        cancellationToken: cancellationToken))
                : await connection.QueryAsync<ChunkRow>(
                    new CommandDefinition(
                        SearchSqlAllDocuments,
                        new { QueryEmbedding = vector, TopK = topK },
                        cancellationToken: cancellationToken));

            return rows.Select(r => new ChunkSearchResult(
                new DocumentChunk
                {
                    Id = r.Id,
                    DocumentId = r.DocumentId,
                    DocumentName = r.DocumentName,
                    ChunkIndex = r.ChunkIndex,
                    PageNumber = r.PageNumber,
                    SectionTitle = r.SectionTitle,
                    Content = r.Content,
                    Embedding = [],
                    CreatedAtUtc = r.CreatedAtUtc,
                },
                r.Similarity)).ToList();
        }
        catch (NpgsqlException ex)
        {
            throw new RagStorageException($"Vector similarity search failed: {ex.Message}", ex);
        }
    }

    private sealed record ChunkRow(
        Guid Id,
        Guid DocumentId,
        string DocumentName,
        int ChunkIndex,
        int? PageNumber,
        string? SectionTitle,
        string Content,
        DateTime CreatedAtUtc,
        double Similarity);
}
