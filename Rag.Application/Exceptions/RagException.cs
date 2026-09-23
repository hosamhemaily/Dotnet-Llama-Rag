namespace Rag.Application.Exceptions;

/// <summary>Base type for well-understood, expected failure modes in the RAG pipeline.</summary>
public abstract class RagException : Exception
{
    protected RagException(string message) : base(message)
    {
    }

    protected RagException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>LlamaParse configuration, request, or parsing failure.</summary>
public sealed class LlamaParseException : RagException
{
    public LlamaParseException(string message) : base(message)
    {
    }

    public LlamaParseException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>Ollama is unreachable, or the requested model is not installed.</summary>
public sealed class OllamaUnavailableException : RagException
{
    public OllamaUnavailableException(string message) : base(message)
    {
    }

    public OllamaUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>The database is unreachable or a query failed unexpectedly.</summary>
public sealed class RagStorageException : RagException
{
    public RagStorageException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
