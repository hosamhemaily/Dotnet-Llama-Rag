namespace Rag.Domain.Models;

/// <summary>
/// Result of parsing a source file (e.g. via LlamaParse). Preserves per-page
/// structure so downstream chunking can keep accurate page metadata.
/// </summary>
public sealed record ParsedDocument
{
    public required string FileName { get; init; }

    /// <summary>Full document content as Markdown, all pages concatenated.</summary>
    public required string Markdown { get; init; }

    public required IReadOnlyList<ParsedPage> Pages { get; init; }
}

/// <summary>One page of a parsed document, as reported by the parser.</summary>
public sealed record ParsedPage
{
    public required int PageNumber { get; init; }

    public required string Content { get; init; }
}
