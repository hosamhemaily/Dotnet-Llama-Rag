namespace Rag.Infrastructure.LlamaParse;

/// <summary>Bound from the "LlamaCloud" configuration section. The API key must come from
/// configuration/user-secrets/environment variables - never hardcode it.</summary>
public sealed class LlamaCloudOptions
{
    public const string SectionName = "LlamaCloud";

    public string BaseUrl { get; set; } = "https://api.cloud.llamaindex.ai";

    /// <summary>API key from https://cloud.llamaindex.ai - set via user-secrets or the LLAMACLOUD_API_KEY env var.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Parse tier: "fast", "cost_effective" (default), "agentic", or "agentic_plus" for harder documents.</summary>
    public string ParseTier { get; set; } = "cost_effective";

    public int PollIntervalSeconds { get; set; } = 3;

    public int MaxPollAttempts { get; set; } = 200;
}
