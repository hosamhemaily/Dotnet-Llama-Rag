using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rag.Application.Exceptions;
using Rag.Application.Interfaces;
using Rag.Domain.Models;

namespace Rag.Infrastructure.LlamaParse;

/// <summary>
/// Parses documents via the LlamaCloud "Parse" v2 REST API: upload the file, start an
/// async parse job, poll until completion, then fetch the per-page markdown result.
/// See https://developers.llamaindex.ai/llamaparse/parse/ for the current API contract.
/// </summary>
public sealed class LlamaParseService : ILlamaParseService
{
    private readonly HttpClient _httpClient;
    private readonly LlamaCloudOptions _options;
    private readonly ILogger<LlamaParseService> _logger;

    public LlamaParseService(HttpClient httpClient, IOptions<LlamaCloudOptions> options, ILogger<LlamaParseService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ParsedDocument> ParseAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new LlamaParseException(
                "LlamaCloud API key is not configured. Set it via 'dotnet user-secrets set \"LlamaCloud:ApiKey\" \"<key>\"' " +
                "(in Rag.Api) or the LLAMACLOUD_API_KEY environment variable. Get a key at https://cloud.llamaindex.ai.");
        }

        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        try
        {
            var fileId = await UploadFileAsync(fileStream, fileName, cancellationToken);
            _logger.LogInformation("Uploaded {FileName} to LlamaCloud as file {FileId}", fileName, fileId);

            var jobId = await StartParseJobAsync(fileId, cancellationToken);
            _logger.LogInformation("Started LlamaParse job {JobId} for {FileName}", jobId, fileName);

            await WaitForCompletionAsync(jobId, cancellationToken);
            _logger.LogInformation("LlamaParse job {JobId} completed", jobId);

            return await FetchResultAsync(jobId, fileName, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlamaParseException($"Could not reach LlamaCloud at {_options.BaseUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LlamaParseException($"LlamaCloud request timed out: {ex.Message}", ex);
        }
    }

    private async Task<string> UploadFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(streamContent, "upload_file", fileName);
        content.Add(new StringContent("parse"), "purpose");

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/files") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "file upload", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return payload.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } id
            ? id
            : throw new LlamaParseException("LlamaCloud file upload response did not include an 'id'.");
    }

    private async Task<string> StartParseJobAsync(string fileId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v2/parse")
        {
            Content = JsonContent.Create(new { file_id = fileId, tier = _options.ParseTier, version = "latest" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "parse job creation", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return payload.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } id
            ? id
            : throw new LlamaParseException("LlamaCloud parse job response did not include an 'id'.");
    }

    private async Task WaitForCompletionAsync(string jobId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.MaxPollAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v2/parse/{jobId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "parse job status", cancellationToken);

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var status = ReadStatus(payload);

            if (status is "COMPLETED" or "SUCCESS")
            {
                return;
            }

            if (status is "FAILED" or "ERROR" or "CANCELLED")
            {
                var errorMessage = ReadErrorMessage(payload) ?? "no additional details provided";
                throw new LlamaParseException($"LlamaParse job {jobId} failed with status '{status}': {errorMessage}");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), cancellationToken);
        }

        throw new LlamaParseException(
            $"LlamaParse job {jobId} did not complete within {_options.MaxPollAttempts * _options.PollIntervalSeconds} seconds.");
    }

    private static string ReadStatus(JsonElement payload)
    {
        if (payload.TryGetProperty("status", out var topStatus) && topStatus.ValueKind == JsonValueKind.String)
        {
            return topStatus.GetString()!.ToUpperInvariant();
        }

        if (payload.TryGetProperty("job", out var job) &&
            job.TryGetProperty("status", out var nestedStatus) &&
            nestedStatus.ValueKind == JsonValueKind.String)
        {
            return nestedStatus.GetString()!.ToUpperInvariant();
        }

        return "UNKNOWN";
    }

    private static string? ReadErrorMessage(JsonElement payload)
    {
        if (payload.TryGetProperty("error_message", out var top) && top.ValueKind == JsonValueKind.String)
        {
            return top.GetString();
        }

        if (payload.TryGetProperty("job", out var job) &&
            job.TryGetProperty("error_message", out var nested) &&
            nested.ValueKind == JsonValueKind.String)
        {
            return nested.GetString();
        }

        return null;
    }

    private async Task<ParsedDocument> FetchResultAsync(string jobId, string fileName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v2/parse/{jobId}?expand=markdown");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "parse result retrieval", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        var pages = new List<ParsedPage>();
        if (payload.TryGetProperty("markdown", out var markdownElement) &&
            markdownElement.TryGetProperty("pages", out var pagesElement) &&
            pagesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var pageElement in pagesElement.EnumerateArray())
            {
                var pageNumber = pageElement.TryGetProperty("page_number", out var pn) && pn.TryGetInt32(out var pnValue)
                    ? pnValue
                    : pages.Count + 1;

                var content = pageElement.TryGetProperty("markdown", out var md) ? md.GetString() ?? string.Empty : string.Empty;

                pages.Add(new ParsedPage { PageNumber = pageNumber, Content = content });
            }
        }

        if (pages.Count == 0)
        {
            throw new LlamaParseException($"LlamaParse returned no page content for job {jobId}. The document may be empty or unsupported.");
        }

        return new ParsedDocument
        {
            FileName = fileName,
            Markdown = string.Join("\n\n", pages.Select(p => p.Content)),
            Pages = pages,
        };
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new LlamaParseException($"LlamaCloud {operation} failed with HTTP {(int)response.StatusCode}: {body}");
    }
}
