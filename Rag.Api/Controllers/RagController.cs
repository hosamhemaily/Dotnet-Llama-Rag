using Microsoft.AspNetCore.Mvc;
using Rag.Application.Interfaces;
using Rag.Domain.Models;

namespace Rag.Api.Controllers;

[ApiController]
[Route("api/rag")]
public sealed class RagController : ControllerBase
{
    private readonly IRagService _ragService;
    private readonly ILogger<RagController> _logger;

    public RagController(IRagService ragService, ILogger<RagController> logger)
    {
        _ragService = ragService;
        _logger = logger;
    }

    /// <summary>Answers a question grounded in previously ingested documents, using vector similarity search + Ollama.</summary>
    [HttpPost("ask")]
    [ProducesResponseType(typeof(RagResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RagResponse>> Ask([FromBody] RagQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received question (topK={TopK}): {Question}", query.TopK, query.Question);

        var response = await _ragService.AskAsync(query, cancellationToken);

        return Ok(response);
    }
}
