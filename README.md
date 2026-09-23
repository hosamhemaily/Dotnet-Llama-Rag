# DotnetLlamaRag

A production-oriented Retrieval-Augmented Generation (RAG) API built with **.NET 9**, **ASP.NET Core**, **LlamaParse/LlamaCloud**, **Ollama** (local LLM + embeddings), **PostgreSQL + pgvector**, and **Dapper**.

```
PDF -> LlamaParse -> chunk (page/section/paragraph aware) -> Ollama embeddings -> pgvector
question -> Ollama embedding -> pgvector similarity search -> Ollama LLM -> structured JSON answer + sources
```

## Architecture

```
                      ┌───────────────────────────────────────────┐
                      │                 Rag.Api                    │
                      │  Controllers: DocumentsController,          │
                      │               RagController                 │
                      │  GlobalExceptionHandler -> ProblemDetails    │
                      └───────────────────┬───────────────────────┘
                                          │ depends on
                      ┌───────────────────▼───────────────────────┐
                      │              Rag.Application                │
                      │  Interfaces: ILlamaParseService,             │
                      │    IEmbeddingService, ILlmService,           │
                      │    IRagRepository, IRagService,              │
                      │    IDocumentIngestionService                 │
                      │  Services: ChunkingService,                  │
                      │    DocumentIngestionService, RagService,     │
                      │    LlmResponseParser                         │
                      └───────────────────┬───────────────────────┘
                                          │ depends on
                      ┌───────────────────▼───────────────────────┐
                      │                Rag.Domain                   │
                      │  Entities: DocumentChunk                     │
                      │  Models: ParsedDocument, RagQuery,           │
                      │          RagResponse                         │
                      └───────────────────▲───────────────────────┘
                                          │ implements Application interfaces
                      ┌───────────────────┴───────────────────────┐
                      │             Rag.Infrastructure               │
                      │  LlamaParse/LlamaParseService (HTTP)         │
                      │  Ollama/OllamaEmbeddingService (HTTP)        │
                      │  Ollama/OllamaLlmService (HTTP to Ollama)    │
                      │  Persistence/RagRepository (Dapper+pgvector) │
                      └───────────────────────────────────────────┘

  External:  LlamaCloud API   |   Ollama (Docker or local)   |   PostgreSQL + pgvector (Docker)
```

**Ingestion pipeline:** `Upload -> LlamaParseService.ParseAsync -> ChunkingService.Chunk -> OllamaEmbeddingService.EmbedBatchAsync -> RagRepository.InsertChunksAsync`

**Query pipeline:** `Ask -> OllamaEmbeddingService.EmbedAsync -> RagRepository.SearchAsync (pgvector cosine search) -> RagService builds a grounded prompt -> OllamaLlmService.GenerateAsync -> LlmResponseParser -> reconcile LLM-cited sources against the real retrieved chunks -> RagResponse`

A key design choice: the LLM is asked to cite `documentName`/`pageNumber` in its JSON, but `content` and `similarity` in the final response always come from the **actual retrieved chunks**, never from the model's own text. Citations that don't match a retrieved chunk are dropped (see `RagService.ReconcileSources`), so the API can't return a hallucinated document name, page number, or similarity score.

## Project layout

```
DotnetLlamaRag/
├── DotnetLlamaRag.sln
├── docker-compose.yml
├── .env.example
├── database/001-init.sql          # pgvector schema
├── Rag.Domain/                    # Entities + models, no dependencies
├── Rag.Application/                # Interfaces + orchestration services
├── Rag.Infrastructure/             # LlamaParse, Ollama, Dapper/pgvector, DI wiring
├── Rag.Api/                        # ASP.NET Core Web API, controllers, Program.cs
└── Rag.Tests/                      # xUnit tests
```

## Prerequisites

- **Windows + PowerShell** (all commands below are PowerShell)
- **.NET 9 SDK**. Check with:
  ```powershell
  dotnet --list-sdks
  ```
  If you don't see a `9.x` entry, install it (no admin rights required):
  ```powershell
  Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile "$env:TEMP\dotnet-install.ps1"
  & "$env:TEMP\dotnet-install.ps1" -Channel 9.0
  ```
  This installs to `%LOCALAPPDATA%\Microsoft\dotnet`. Either add that to your `PATH` permanently, or prefix each command below with:
  ```powershell
  $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:PATH"
  ```
- **Docker Desktop** (for PostgreSQL + pgvector, and optionally Ollama). Verify:
  ```powershell
  docker --version
  docker compose version
  ```
  If Docker Desktop isn't running, start it and wait for the whale icon to settle before continuing:
  ```powershell
  Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"
  ```
- **A LlamaCloud account and API key** (free tier available) — see step 8 below.
- **Ollama**, either running natively on Windows (https://ollama.com/download) or via the Docker Compose file in this repo (recommended, no separate install needed).

## Step-by-step setup

### 1. Start PostgreSQL (+ pgvector)

From the repository root:

```powershell
docker compose up -d postgres
```

This starts `pgvector/pgvector:pg16` and automatically runs `database/001-init.sql` on first start (via `docker-entrypoint-initdb.d`), creating the `vector` extension, the `document_chunks` table, and the cosine-similarity index. Data persists in the `postgres-data` Docker volume across restarts.

Verify it's healthy:

```powershell
docker compose ps
docker exec dotnetllamarag-postgres pg_isready -U rag -d rag
```

If you ever need to re-run the init script (e.g. after editing it), the script only runs automatically on a **fresh** volume. To force it:

```powershell
docker compose down -v   # WARNING: this deletes all ingested data
docker compose up -d postgres
```

Or apply it manually to an existing database:

```powershell
Get-Content database\001-init.sql | docker exec -i dotnetllamarag-postgres psql -U rag -d rag
```

### 2. Start Ollama and pull the models

**Option A - Docker (recommended, matches this repo's default config):**

```powershell
docker compose up -d ollama
docker exec dotnetllamarag-ollama ollama pull nomic-embed-text
docker exec dotnetllamarag-ollama ollama pull qwen3:8b
```

**Option B - native Ollama on Windows:**

```powershell
ollama serve   # if not already running as a background service
ollama pull nomic-embed-text
ollama pull qwen3:8b
```

Either way, verify the models are present and Ollama is reachable:

```powershell
curl.exe http://localhost:11434/api/tags
```

> `qwen3:8b` is a ~5 GB download and needs a reasonably capable GPU/CPU to run at usable speed. If your machine is limited, swap in a smaller model such as `qwen3:1.7b` or `llama3.2:3b` by changing `Ollama:ChatModel` in `Rag.Api/appsettings.json` — no other code changes are needed.

### 3. Get a LlamaCloud API key

1. Go to https://cloud.llamaindex.ai and sign up (free tier available).
2. Open **Settings -> API Keys** and create a new key.
3. Keep it handy for the next step. **Never commit this key.**

### 4. Configure the API key

Preferred (local dev, keeps the key out of any file in the repo):

```powershell
cd Rag.Api
dotnet user-secrets set "LlamaCloud:ApiKey" "llx-your-real-key-here"
cd ..
```

Alternative (environment variable - also what `docker-compose.yml`'s optional `api` service uses):

```powershell
$env:LLAMACLOUD_API_KEY = "llx-your-real-key-here"
```

`Rag.Api/appsettings.json` intentionally ships with `LlamaCloud:ApiKey` set to an empty string. If it's still empty when you call the upload endpoint, the API returns a clear `502` error telling you to set it - it will not silently fail.

### 5. Restore, build, and test

```powershell
dotnet restore
dotnet build
dotnet test
```

All 16 unit tests (chunking behavior + LLM JSON response parsing + RAG orchestration) should pass without needing Docker, Ollama, or a LlamaCloud key - they use in-memory fakes for every external dependency.

### 6. Run the API

```powershell
dotnet run --project Rag.Api
```

By default this listens on `https://localhost:7xxx` and `http://localhost:5xxx` (the exact ports are printed on startup and also live in `Rag.Api/Properties/launchSettings.json`).

### 7. Open Swagger

Navigate to:

```
http://localhost:<port>/swagger
```

You'll see the two endpoints: `POST /api/documents/upload` and `POST /api/rag/ask`.

### 8. Upload a document

Via Swagger UI ("Try it out" on `/api/documents/upload"), or via curl:

```powershell
curl.exe -X POST "http://localhost:5000/api/documents/upload" -F "file=@C:\path\to\your.pdf"
```

Response:

```json
{
  "documentId": "b3f2b7b0-....",
  "documentName": "your.pdf",
  "chunkCount": 42,
  "pageCount": 12
}
```

Behind the scenes: the API uploads the file to LlamaCloud, starts an async parse job, polls until it completes, chunks the resulting per-page Markdown (page -> section -> paragraph -> size-bounded chunk with overlap), embeds every chunk with `nomic-embed-text` via Ollama, and inserts them into `document_chunks` with their vectors.

### 9. Ask a question

```powershell
curl.exe -X POST "http://localhost:5000/api/rag/ask" `
  -H "Content-Type: application/json" `
  -d '{ \"question\": \"What is this document about?\", \"topK\": 5 }'
```

Response:

```json
{
  "answer": "This document describes ...",
  "sources": [
    {
      "documentName": "your.pdf",
      "pageNumber": 3,
      "content": "…the exact chunk text that was retrieved…",
      "similarity": 0.83
    }
  ]
}
```

If nothing relevant is found, `answer` says so explicitly and `sources` is empty - the API never calls the LLM to "make something up" when retrieval comes back empty.

## How the RAG pipeline works

### Ingestion (`DocumentIngestionService`)

1. **Parse** (`LlamaParseService`): uploads the file to LlamaCloud (`POST /api/v1/files`), starts a parse job (`POST /api/v2/parse`), polls (`GET /api/v2/parse/{id}`) until `COMPLETED`, then fetches per-page Markdown (`GET /api/v2/parse/{id}?expand=markdown`). Real page numbers come from LlamaParse's response (`markdown.pages[].page_number`) - the code does **not** assume everything is page 1.
2. **Chunk** (`ChunkingService`): for each page, splits on Markdown headers into sections (keeping the section title), splits each section into paragraphs, then packs paragraphs into chunks up to `Chunking:MaxChunkSizeChars` characters with `Chunking:OverlapChars` characters of overlap carried into the next chunk. Every chunk keeps its page number, section title, and document name.
3. **Embed** (`OllamaEmbeddingService`): calls Ollama's `POST /api/embed` with the configured `Ollama:EmbeddingModel`, batching all chunks from a document in one request.
4. **Persist** (`RagRepository`): inserts all chunks (text + metadata + `vector` embedding) into `document_chunks` in a single transaction via Dapper, using the `Pgvector.Dapper` type handler.

### Query (`RagService`)

1. Embed the question with the same Ollama embedding model.
2. `RagRepository.SearchAsync` runs `ORDER BY embedding <=> @queryEmbedding LIMIT @topK` (pgvector cosine distance operator, backed by the `ivfflat`/`vector_cosine_ops` index), returning `1 - distance` as similarity.
3. If nothing is retrieved, return a fixed "not available" answer without calling the LLM.
4. Otherwise, build a prompt containing every retrieved chunk tagged with its `documentName`/`pageNumber`, and a system prompt that instructs the model to answer only from context and return JSON only (see `RagService.SystemPrompt`).
5. `OllamaLlmService` sends the prompt to Ollama's `POST /api/chat` as a `system` + `user` message pair.
6. `LlmResponseParser` robustly extracts JSON from the raw completion, tolerating ` ```json ` code fences or incidental surrounding text.
7. `RagService.ReconcileSources` matches each LLM-cited `(documentName, pageNumber)` back to an actually-retrieved chunk and uses **that chunk's real content and similarity score** in the response - a citation that doesn't match anything retrieved is dropped rather than trusted. If the model cited nothing that matches, the API falls back to returning all retrieved chunks as sources.

## Changing the embedding model

The vector column is fixed-width (`VECTOR(768)` in `database/001-init.sql`), so the embedding model's output dimension **must** match it exactly. To switch models:

1. Pick a new model and pull it: `ollama pull <model>`.
2. Update `Rag.Api/appsettings.json`:
   ```json
   "Ollama": {
     "EmbeddingModel": "<model>",
     "EmbeddingDimension": <its output dimension>
   }
   ```
3. Update the column definition in `database/001-init.sql` to `VECTOR(<its output dimension>)`.
4. Since pgvector cannot mix vector dimensions in one column, you must re-create the table (or the column) and **re-ingest every document** - old embeddings are not compatible with the new model:
   ```powershell
   docker compose down -v
   docker compose up -d postgres
   ```

Common model dimensions: `nomic-embed-text` = 768, `mxbai-embed-large` = 1024, `all-minilm` = 384. Always verify with `ollama show <model>` rather than assuming.

## Configuration reference

All non-secret settings live in `Rag.Api/appsettings.json`:

| Section | Key | Default | Meaning |
|---|---|---|---|
| `ConnectionStrings` | `Postgres` | `Host=localhost;Port=5432;Database=rag;Username=rag;Password=rag` | Npgsql connection string |
| `LlamaCloud` | `BaseUrl` | `https://api.cloud.llamaindex.ai` | LlamaCloud API base URL |
| `LlamaCloud` | `ApiKey` | *(empty)* | **Set via user-secrets or env var, never here** |
| `LlamaCloud` | `ParseTier` | `balanced` | LlamaParse quality tier (e.g. `balanced`, `agentic`) |
| `LlamaCloud` | `PollIntervalSeconds` / `MaxPollAttempts` | `3` / `200` | Job-polling cadence and timeout (10 min default) |
| `Ollama` | `BaseUrl` | `http://localhost:11434` | Ollama server URL |
| `Ollama` | `EmbeddingModel` / `EmbeddingDimension` | `nomic-embed-text` / `768` | Must stay in sync with each other and the DB schema |
| `Ollama` | `ChatModel` | `qwen3:8b` | Chat/completion model |
| `Ollama` | `Temperature` | `0.1` | Low temperature keeps answers grounded and deterministic |
| `Chunking` | `MaxChunkSizeChars` / `OverlapChars` | `1500` / `200` | Chunk packing parameters |

## Error handling

| Condition | HTTP status | Notes |
|---|---|---|
| Missing LlamaCloud API key | 502 | Checked before any network call |
| LlamaParse request/job failure or timeout | 502 | Includes LlamaCloud's own error message |
| Ollama unreachable | 503 | e.g. Docker container not started |
| Ollama model not pulled | 503 | Message tells you the exact `ollama pull` command to run |
| PostgreSQL unreachable / query failure | 503 | |
| Empty uploaded file | 400 | |
| Document produced no parseable content/chunks | 400 | |
| Empty question | 400 | |
| Invalid/unparseable JSON from the LLM | 502 | The raw response is logged; the error is never swallowed |
| Embedding dimension mismatch | 400 | Raised during ingestion before any DB write is attempted |

All of the above are mapped centrally in `Rag.Api/GlobalExceptionHandler.cs` to an RFC 7807 `ProblemDetails` body.

## Troubleshooting

- **`LlamaCloud API key is not configured`** - you skipped step 4. Run the `dotnet user-secrets set` command from the `Rag.Api` directory (the working directory matters for user-secrets).
- **`Could not reach Ollama at http://localhost:11434`** - Ollama isn't running. If using Docker, run `docker compose ps` and `docker compose up -d ollama`. If native, run `ollama serve`.
- **`Ollama model '...' is not installed`** - run the `ollama pull <model>` command shown in the error message (also see step 2).
- **`Vector similarity search failed` / connection errors from PostgreSQL** - confirm `docker compose ps` shows `postgres` as healthy, and that `ConnectionStrings:Postgres` in `appsettings.json` matches the container's actual host/port/credentials.
- **Insert fails with a dimension-related PostgreSQL error** - your `Ollama:EmbeddingDimension` doesn't match the `VECTOR(N)` column. See "Changing the embedding model" above.
- **LlamaParse job never completes / times out** - very large or complex PDFs can take minutes. Increase `LlamaCloud:MaxPollAttempts` in `appsettings.json` if needed.
- **The LLM's JSON gets rejected** - some very small/undertrained chat models ignore "JSON only" instructions. Prefer instruction-tuned models (`qwen3:8b` and similar are reliable); the parser already tolerates code fences and minor surrounding text, but a model that produces free-form prose with no JSON at all will still fail with a 502 by design (the error is surfaced, not hidden).
- **Docker Desktop daemon not reachable** (`open //./pipe/dockerDesktopLinuxEngine`) - start Docker Desktop and wait for it to fully initialize before running `docker compose` commands.
- **`docker info` returns `500 Internal Server Error` for `dockerDesktopLinuxEngine`** - Docker Desktop's Windows-side processes are running but its Linux container backend (WSL2 or Hyper-V) isn't working. Run `wsl --status`; if WSL reports it isn't installed/configured, run `wsl --install` (requires a reboot) or switch Docker Desktop to the Hyper-V backend in its settings, then restart Docker Desktop. This cannot be fixed from inside a sandboxed VM without nested virtualization support.

## Testing

```powershell
dotnet test
```

Covers:
- `ChunkingServiceTests` - page/section/paragraph-aware splitting, metadata propagation, overlap, oversized-paragraph splitting, blank-page skipping.
- `LlmResponseParserTests` - plain JSON, code-fence-wrapped JSON, JSON with surrounding commentary, missing/invalid JSON error paths.
- `RagServiceTests` - empty-retrieval short-circuit, source reconciliation against real retrieved chunks, fallback when the LLM cites an unknown source, empty-question validation.

These tests use small hand-written fakes for `IEmbeddingService`, `IRagRepository`, and `ILlmService` - no Docker, network, or database access is required to run the suite.

## Known limitations

- **LlamaCloud dependency**: actually ingesting a document requires a real LlamaCloud account and API key (step 3/4). This was not something we could provision or verify end-to-end in this environment - the LlamaParse REST calls were implemented against LlamaCloud's current documented `v1`/`v2` API contract, but a live upload/parse/ask round-trip through a real LlamaCloud account has not been executed here.
- **Docker containers could not be started in this environment**: Docker Desktop was installed and its CLI/engine processes did start, but its Linux container backend kept returning `500 Internal Server Error` on every `docker info`/`docker compose up`, and `wsl --status` showed WSL isn't fully set up in this sandboxed VM (nested virtualization for WSL2/Hyper-V is likely unavailable here). As a result, **PostgreSQL and Ollama were never actually brought up, and `nomic-embed-text`/`qwen3:8b` were never pulled or exercised live** - the ingestion and query pipelines have not been run end-to-end against a real database or a real Ollama instance in this environment. On a normal Windows machine with WSL2/Docker Desktop working, `docker compose up -d postgres ollama` from this README should work as documented.
- **What *was* verified in this environment**: `dotnet restore` / `dotnet build` / `dotnet test` all succeed (16/16 unit tests pass) against the actual current NuGet package versions (Npgsql 10.0.3, Pgvector 0.3.1/0.3.2, Pgvector.Dapper 0.3.1, Dapper 2.1.86, Swashbuckle.AspNetCore 10.2.3). The LlamaParse and Ollama REST contracts were verified against current official documentation (see the source code comments/links in `Rag.Infrastructure`), not against live calls. Ollama's chat requests use its `format` field (a JSON Schema) to grammar-constrain the model's output to the expected `{answer, sources}` shape, rather than relying on prompt instructions alone.
- **.NET 9 SDK**: this machine only had the .NET 10 SDK preinstalled; the .NET 9 SDK (9.0.318) was downloaded via the official `dotnet-install.ps1` script into `%LOCALAPPDATA%\Microsoft\dotnet` specifically to honor the ".NET 9" requirement. If your machine already has .NET 9 on `PATH`, none of that is necessary.
- **IVFFlat index quality**: the pgvector index uses `lists = 100`, a reasonable default for small/medium collections; for large corpora, rebuild the index with more lists (roughly `sqrt(row_count)`) as documented in `database/001-init.sql`.
