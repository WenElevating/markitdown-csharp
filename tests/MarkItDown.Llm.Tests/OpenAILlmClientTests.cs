using MarkItDown.Llm;

namespace MarkItDown.Llm.Tests;

public sealed class OpenAILlmClientTests
{
    [Fact]
    public void Constructor_RequiresApiKey()
    {
        Assert.Throws<ArgumentException>(() =>
            new OpenAILlmClient(new LlmClientOptions { ApiKey = null! }));
    }

    [Fact]
    public void Constructor_ThrowsOnNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OpenAILlmClient(null!));
    }

    [Fact]
    public void Constructor_AcceptsValidOptions()
    {
        var options = new LlmClientOptions { ApiKey = "test-key" };
        var client = new OpenAILlmClient(options);

        Assert.NotNull(client);
    }

    [Fact]
    public void Constructor_UsesDefaultModel()
    {
        var options = new LlmClientOptions { ApiKey = "test-key" };

        Assert.Equal("gpt-4o", options.Model);
    }

    [Fact]
    public void Constructor_AcceptsCustomEndpoint()
    {
        var options = new LlmClientOptions
        {
            ApiKey = "test-key",
            Endpoint = "https://my-openai-proxy.example.com/v1",
        };

        var client = new OpenAILlmClient(options);

        Assert.NotNull(client);
    }
}
