using Rag.Application.Services;

namespace Rag.Tests;

public class LlmResponseParserTests
{
    [Fact]
    public void Parse_PlainJson_ReturnsAnswerAndSources()
    {
        const string raw = """
            { "answer": "The document is a user manual.", "sources": [ { "documentName": "manual.pdf", "pageNumber": 1 } ] }
            """;

        var result = LlmResponseParser.Parse(raw);

        Assert.Equal("The document is a user manual.", result.Answer);
        Assert.Single(result.Sources);
        Assert.Equal("manual.pdf", result.Sources[0].DocumentName);
        Assert.Equal(1, result.Sources[0].PageNumber);
    }

    [Fact]
    public void Parse_JsonWrappedInMarkdownCodeFence_IsUnwrapped()
    {
        const string raw = """
            Here is the answer:
            ```json
            { "answer": "42", "sources": [] }
            ```
            """;

        var result = LlmResponseParser.Parse(raw);

        Assert.Equal("42", result.Answer);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public void Parse_JsonWithSurroundingCommentary_ExtractsBraceDelimitedJson()
    {
        const string raw = "Sure! " + "{ \"answer\": \"only from context\", \"sources\": [] }" + " Let me know if you need more.";

        var result = LlmResponseParser.Parse(raw);

        Assert.Equal("only from context", result.Answer);
    }

    [Fact]
    public void Parse_MissingAnswerField_Throws()
    {
        const string raw = """{ "sources": [] }""";

        Assert.Throws<LlmResponseFormatException>(() => LlmResponseParser.Parse(raw));
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsWithRawResponsePreserved()
    {
        const string raw = "not json at all";

        var ex = Assert.Throws<LlmResponseFormatException>(() => LlmResponseParser.Parse(raw));

        Assert.Equal(raw, ex.RawResponse);
    }

    [Fact]
    public void Parse_EmptyResponse_Throws()
    {
        Assert.Throws<LlmResponseFormatException>(() => LlmResponseParser.Parse(""));
    }

    [Fact]
    public void Parse_SourceWithoutDocumentName_IsSkipped()
    {
        const string raw = """
            { "answer": "x", "sources": [ { "pageNumber": 2 }, { "documentName": "a.pdf", "pageNumber": null } ] }
            """;

        var result = LlmResponseParser.Parse(raw);

        Assert.Single(result.Sources);
        Assert.Equal("a.pdf", result.Sources[0].DocumentName);
        Assert.Null(result.Sources[0].PageNumber);
    }
}
