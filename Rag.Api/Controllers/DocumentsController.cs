using Microsoft.AspNetCore.Mvc;
using Rag.Application.Interfaces;

namespace Rag.Api.Controllers;

[ApiController]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly IDocumentIngestionService _ingestionService;
    private readonly ILogger<DocumentsController> _logger;

    public DocumentsController(IDocumentIngestionService ingestionService, ILogger<DocumentsController> logger)
    {
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>Uploads a document (PDF, etc.), parses it via LlamaParse, chunks it, embeds it with Ollama, and stores it in pgvector.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100_000_000)]
    [ProducesResponseType(typeof(DocumentUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentUploadResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem("No file was uploaded, or the uploaded file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        _logger.LogInformation("Received upload request for {FileName} ({Length} bytes)", file.FileName, file.Length);

        await using var stream = file.OpenReadStream();
        var result = await _ingestionService.IngestAsync(stream, file.FileName, cancellationToken);

        return Ok(new DocumentUploadResponse(result.DocumentId, result.DocumentName, result.ChunkCount, result.PageCount));
    }
}

public sealed record DocumentUploadResponse(Guid DocumentId, string DocumentName, int ChunkCount, int PageCount);
