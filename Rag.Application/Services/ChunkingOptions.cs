namespace Rag.Application.Services;

/// <summary>Bound from the "Chunking" configuration section.</summary>
public sealed class ChunkingOptions
{
    /// <summary>Soft maximum number of characters per chunk.</summary>
    public int MaxChunkSizeChars { get; set; } = 1500;

    /// <summary>Number of trailing characters from the previous chunk repeated at the start of the next one.</summary>
    public int OverlapChars { get; set; } = 200;
}
