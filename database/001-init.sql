-- DotnetLlamaRag database schema.
--
-- IMPORTANT: the vector(768) dimension below must match Ollama:EmbeddingDimension in
-- Rag.Api/appsettings.json, which in turn must match the actual output dimension of the
-- model configured in Ollama:EmbeddingModel. The default model, nomic-embed-text, produces
-- 768-dimensional vectors. If you change the embedding model, you must update the dimension
-- here (see the README section "Changing the embedding model") and re-ingest all documents,
-- since pgvector cannot mix vectors of different dimensions in the same column.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS document_chunks (
    id              UUID PRIMARY KEY,
    document_id     UUID NOT NULL,
    document_name   TEXT NOT NULL,
    chunk_index     INTEGER NOT NULL,
    page_number     INTEGER NULL,
    section_title   TEXT NULL,
    content         TEXT NOT NULL,
    embedding       VECTOR(768) NOT NULL,
    created_at_utc  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Lookups by document (e.g. filtering search to one document, deleting a document's chunks).
CREATE INDEX IF NOT EXISTS ix_document_chunks_document_id
    ON document_chunks (document_id);

-- Approximate nearest-neighbour index for cosine similarity search (the `<=>` operator).
-- IVFFlat requires an estimate of the number of rows; 100 lists is a reasonable default for
-- small-to-medium collections. Rebuild with more lists (roughly sqrt(row_count)) as the
-- table grows, e.g.: REINDEX INDEX ix_document_chunks_embedding_cosine;
CREATE INDEX IF NOT EXISTS ix_document_chunks_embedding_cosine
    ON document_chunks
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);
