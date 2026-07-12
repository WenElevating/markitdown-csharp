namespace MarkItDown.Core;

public sealed class LlmVisionAnalyzerAdapter(ILlmClient client) : IVisionAnalyzer
{
    public async Task<VisionAnalysisResult> AnalyzeAsync(VisionTaskType task, Stream input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var prompt = task switch
        {
            VisionTaskType.OcrPage => "Transcribe all readable text in this page. Return only the transcription.",
            VisionTaskType.DescribeFigure => "Describe this figure accurately for an accessible Markdown document.",
            VisionTaskType.AnalyzeChart => "Extract the chart's title, axes, legend, and values as structured plain text.",
            VisionTaskType.RecoverTable => "Recover this table as rows and columns. Do not invent missing cells.",
            _ => "Describe the supplied document image accurately."
        };
        var text = await client.CompleteAsync(prompt, buffer.ToArray(), "application/octet-stream", cancellationToken);
        return new VisionAnalysisResult(text, 0.5, "ILlmClient", "configured", "legacy-adapter-v1");
    }
}
