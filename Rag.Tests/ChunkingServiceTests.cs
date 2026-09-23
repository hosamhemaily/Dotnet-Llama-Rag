using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rag.Application.Services;
using Rag.Domain.Models;

namespace Rag.Tests;

public class ChunkingServiceTests
{
    private static ChunkingService CreateService(int maxChunkSizeChars = 1500, int overlapChars = 200)
    {
        var options = Options.Create(new ChunkingOptions
        {
            MaxChunkSizeChars = maxChunkSizeChars,
            OverlapChars = overlapChars,
        });

        return new ChunkingService(options, NullLogger<ChunkingService>.Instance);
    }

    [Fact]
    public void Chunk_PreservesPageNumberAndSectionTitle()
    {
        var service = CreateService();
        var document = new ParsedDocument
        {
            FileName = "manual.pdf",
            Markdown = "ignored",
            Pages =
            [
                new ParsedPage
                {
                    PageNumber = 4,
                    Content = "# Installation\n\nFollow these steps to install the product.\n\nStep one is to unpack the box.",
                },
            ],
        };

        var chunks = service.Chunk(document, Guid.NewGuid());

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c =>
        {
            Assert.Equal(4, c.PageNumber);
            Assert.Equal("Installation", c.SectionTitle);
            Assert.Equal("manual.pdf", c.DocumentName);
        });
    }

    [Fact]
    public void Chunk_AssignsSequentialChunkIndexesAcrossPages()
    {
        var service = CreateService();
        var document = new ParsedDocument
        {
            FileName = "doc.pdf",
            Markdown = "ignored",
            Pages =
            [
                new ParsedPage { PageNumber = 1, Content = "First page paragraph." },
                new ParsedPage { PageNumber = 2, Content = "Second page paragraph." },
            ],
        };

        var chunks = service.Chunk(document, Guid.NewGuid());

        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal(2, chunks[1].PageNumber);
    }

    [Fact]
    public void Chunk_SplitsLongContentIntoMultipleChunksWithOverlap()
    {
        var service = CreateService(maxChunkSizeChars: 100, overlapChars: 20);

        var paragraph1 = string.Join(" ", Enumerable.Repeat("alpha", 20)); // ~120 chars
        var paragraph2 = string.Join(" ", Enumerable.Repeat("beta", 20));  // ~99 chars

        var document = new ParsedDocument
        {
            FileName = "big.pdf",
            Markdown = "ignored",
            Pages = [new ParsedPage { PageNumber = 1, Content = $"{paragraph1}\n\n{paragraph2}" }],
        };

        var chunks = service.Chunk(document, Guid.NewGuid());

        Assert.True(chunks.Count > 1, "Expected content longer than MaxChunkSizeChars to be split into multiple chunks.");
        Assert.All(chunks, c => Assert.True(c.Content.Length <= 100 + 20, "Each chunk should stay close to the configured max size."));
    }

    [Fact]
    public void Chunk_SkipsBlankPages()
    {
        var service = CreateService();
        var document = new ParsedDocument
        {
            FileName = "sparse.pdf",
            Markdown = "ignored",
            Pages =
            [
                new ParsedPage { PageNumber = 1, Content = "   " },
                new ParsedPage { PageNumber = 2, Content = "Actual content here." },
            ],
        };

        var chunks = service.Chunk(document, Guid.NewGuid());

        Assert.Single(chunks);
        Assert.Equal(2, chunks[0].PageNumber);
    }

    [Fact]
    public void Chunk_EmptyDocument_ReturnsNoChunks()
    {
        var service = CreateService();
        var document = new ParsedDocument
        {
            FileName = "empty.pdf",
            Markdown = "ignored",
            Pages = [],
        };

        var chunks = service.Chunk(document, Guid.NewGuid());

        Assert.Empty(chunks);
    }
}
