using System.ClientModel;
using MarkItDown.Core;
using OpenAI;
using OpenAI.Chat;

namespace MarkItDown.Llm;

/// <summary>
/// ILlmClient implementation using the OpenAI .NET SDK.
/// </summary>
public sealed class OpenAILlmClient : ILlmClient
{
    private readonly ChatClient _chatClient;

    public OpenAILlmClient(LlmClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ArgumentException("ApiKey is required.", nameof(options));
        }

        OpenAIClient openAIClient = CreateOpenAIClient(options);
        _chatClient = openAIClient.GetChatClient(options.Model);
    }

    public async Task<string> CompleteAsync(
        string prompt,
        byte[]? imageData = null,
        string? imageMimeType = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        List<ChatMessage> messages = [BuildUserMessage(prompt, imageData, imageMimeType)];

        ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, cancellationToken: ct);
        return completion.Content[0].Text;
    }

    private static UserChatMessage BuildUserMessage(
        string prompt,
        byte[]? imageData,
        string? imageMimeType)
    {
        if (imageData is not null && imageData.Length > 0)
        {
            string mimeType = imageMimeType ?? "image/png";
            BinaryData imageBinaryData = BinaryData.FromBytes(imageData);

            List<ChatMessageContentPart> parts =
            [
                ChatMessageContentPart.CreateTextPart(prompt),
                ChatMessageContentPart.CreateImagePart(imageBinaryData, mimeType),
            ];

            return new UserChatMessage(parts);
        }

        return new UserChatMessage(prompt);
    }

    private static OpenAIClient CreateOpenAIClient(LlmClientOptions options)
    {
        if (options.Endpoint is not null)
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(options.Endpoint),
            };
            return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
        }

        return new OpenAIClient(options.ApiKey);
    }
}
