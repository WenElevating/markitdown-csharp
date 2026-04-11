namespace MarkItDown.Core;

public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, byte[]? imageData = null, string? imageMimeType = null, CancellationToken ct = default);
}
