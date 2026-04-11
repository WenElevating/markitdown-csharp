namespace MarkItDown.Llm;

/// <summary>
/// Configuration options for the OpenAI LLM client.
/// </summary>
public sealed record LlmClientOptions
{
    public required string ApiKey { get; init; }

    public string Model { get; init; } = "gpt-4o";

    /// <summary>
    /// Optional custom endpoint URI (e.g., for Azure OpenAI or a proxy).
    /// </summary>
    public string? Endpoint { get; init; }
}
