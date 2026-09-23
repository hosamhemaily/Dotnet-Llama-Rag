using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Rag.Application.Exceptions;
using Rag.Application.Services;

namespace Rag.Api;

/// <summary>
/// Maps well-understood pipeline failures to meaningful HTTP status codes and a
/// ProblemDetails body, so callers get an actionable response instead of a bare 500.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            LlamaParseException => (StatusCodes.Status502BadGateway, "LlamaParse error"),
            OllamaUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Ollama unavailable"),
            RagStorageException => (StatusCodes.Status503ServiceUnavailable, "Database unavailable"),
            LlmResponseFormatException => (StatusCodes.Status502BadGateway, "The LLM returned an unparseable response"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception while processing {Path}", httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "{Title} while processing {Path}", title, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = exception.Message,
                Instance = httpContext.Request.Path,
            },
            cancellationToken);

        return true;
    }
}
