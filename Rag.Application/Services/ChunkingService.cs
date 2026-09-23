using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rag.Domain.Entities;
using Rag.Domain.Models;

namespace Rag.Application.Services;

/// <summary>
/// Splits a parsed document into overlapping chunks while preserving structural
/// metadata (page number, section title). Structure is respected top-down:
/// document -> pages -> markdown sections -> paragraphs -> size-bounded chunks.
/// </summary>
public sealed class ChunkingService
{
    private static readonly Regex HeaderRegex = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly ChunkingOptions _options;
    private readonly ILogger<ChunkingService> _logger;

    public ChunkingService(IOptions<ChunkingOptions> options, ILogger<ChunkingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyList<DocumentChunk> Chunk(ParsedDocument document, Guid documentId)
    {
        var chunks = new List<DocumentChunk>();
        var chunkIndex = 0;

        foreach (var page in document.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Content))
            {
                continue;
            }

            foreach (var section in SplitIntoSections(page.Content))
            {
                foreach (var chunkText in PackParagraphs(SplitIntoParagraphs(section.Body)))
                {
                    chunks.Add(new DocumentChunk
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = documentId,
                        DocumentName = document.FileName,
                        ChunkIndex = chunkIndex++,
                        PageNumber = page.PageNumber,
                        SectionTitle = section.Title,
                        Content = chunkText,
                        Embedding = [],
                    });
                }
            }
        }

        _logger.LogInformation(
            "Chunked document {DocumentName} into {ChunkCount} chunks across {PageCount} pages",
            document.FileName, chunks.Count, document.Pages.Count);

        return chunks;
    }

    private static IEnumerable<(string? Title, string Body)> SplitIntoSections(string pageContent)
    {
        var matches = HeaderRegex.Matches(pageContent);

        if (matches.Count == 0)
        {
            yield return (null, pageContent);
            yield break;
        }

        var firstHeaderStart = matches[0].Index;
        if (firstHeaderStart > 0)
        {
            var preamble = pageContent[..firstHeaderStart];
            if (!string.IsNullOrWhiteSpace(preamble))
            {
                yield return (null, preamble);
            }
        }

        for (var i = 0; i < matches.Count; i++)
        {
            var current = matches[i];
            var title = current.Groups[2].Value.Trim();
            var bodyStart = current.Index + current.Length;
            var bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : pageContent.Length;
            var body = pageContent[bodyStart..bodyEnd].Trim();

            if (!string.IsNullOrWhiteSpace(body))
            {
                yield return (title, body);
            }
        }
    }

    private static IReadOnlyList<string> SplitIntoParagraphs(string sectionBody)
    {
        return Regex.Split(sectionBody, @"\r?\n\s*\r?\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
    }

    private IEnumerable<string> PackParagraphs(IReadOnlyList<string> paragraphs)
    {
        var maxSize = _options.MaxChunkSizeChars;
        var overlap = Math.Min(_options.OverlapChars, maxSize / 2);

        var current = new StringBuilder();

        foreach (var rawParagraph in paragraphs)
        {
            foreach (var paragraph in SplitOversizedParagraph(rawParagraph, maxSize))
            {
                if (current.Length > 0 && current.Length + 2 + paragraph.Length > maxSize)
                {
                    var finished = current.ToString();
                    yield return finished;
                    current.Clear();
                    current.Append(TakeOverlapTail(finished, overlap));
                }

                if (current.Length > 0)
                {
                    current.Append("\n\n");
                }

                current.Append(paragraph);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static IEnumerable<string> SplitOversizedParagraph(string paragraph, int maxSize)
    {
        if (paragraph.Length <= maxSize)
        {
            yield return paragraph;
            yield break;
        }

        for (var offset = 0; offset < paragraph.Length; offset += maxSize)
        {
            yield return paragraph.Substring(offset, Math.Min(maxSize, paragraph.Length - offset));
        }
    }

    private static string TakeOverlapTail(string text, int overlap)
    {
        if (overlap <= 0 || text.Length <= overlap)
        {
            return string.Empty;
        }

        var tail = text[^overlap..];
        var spaceIndex = tail.IndexOf(' ');
        return spaceIndex >= 0 ? tail[(spaceIndex + 1)..] : tail;
    }
}
