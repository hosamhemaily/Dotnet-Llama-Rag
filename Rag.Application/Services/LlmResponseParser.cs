using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rag.Application.Services;

public sealed record LlmSourceRef(string DocumentName, int? PageNumber);

public sealed record LlmAnswer(string Answer, IReadOnlyList<LlmSourceRef> Sources);

/// <summary>
/// Thrown when the LLM's response cannot be interpreted as the expected JSON shape.
/// Callers should surface this as an error rather than silently guessing an answer.
/// </summary>
public sealed class LlmResponseFormatException : Exception
{
    public LlmResponseFormatException(string message, string rawResponse) : base(message)
    {
        RawResponse = rawResponse;
    }

    public string RawResponse { get; }
}

/// <summary>
/// Robustly extracts the structured JSON answer from a raw LLM completion, tolerating
/// markdown code fences and incidental surrounding text that some models still emit
/// despite being instructed to return JSON only.
/// </summary>
public static class LlmResponseParser
{
    private static readonly Regex CodeFenceRegex = new(
        @"```(?:json)?\s*(?<body>[\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static LlmAnswer Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            throw new LlmResponseFormatException("The LLM returned an empty response.", rawResponse ?? string.Empty);
        }

        var candidate = ExtractJsonCandidate(rawResponse);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(candidate);
        }
        catch (JsonException ex)
        {
            throw new LlmResponseFormatException(
                $"The LLM response could not be parsed as JSON: {ex.Message}", rawResponse);
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("answer", out var answerElement) || answerElement.ValueKind != JsonValueKind.String)
            {
                throw new LlmResponseFormatException(
                    "The LLM response JSON did not contain a string 'answer' field.", rawResponse);
            }

            var answer = answerElement.GetString() ?? string.Empty;
            var sources = new List<LlmSourceRef>();

            if (root.TryGetProperty("sources", out var sourcesElement) && sourcesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sourcesElement.EnumerateArray())
                {
                    var documentName = TryGetString(item, "documentName");
                    if (documentName is null)
                    {
                        continue;
                    }

                    int? pageNumber = TryGetNullableInt(item, "pageNumber");
                    sources.Add(new LlmSourceRef(documentName, pageNumber));
                }
            }

            return new LlmAnswer(answer, sources);
        }
    }

    private static string ExtractJsonCandidate(string rawResponse)
    {
        var trimmed = rawResponse.Trim();

        var fenceMatch = CodeFenceRegex.Match(trimmed);
        if (fenceMatch.Success)
        {
            return fenceMatch.Groups["body"].Value.Trim();
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? TryGetNullableInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            _ => null,
        };
    }
}
