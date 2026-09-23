using Rag.Domain.Models;

namespace Rag.Application.Interfaces;

/// <summary>Parses a source file into structured, page-aware Markdown via LlamaCloud/LlamaParse.</summary>
public interface ILlamaParseService
{
    Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName, CancellationToken cancellationToken);
}
