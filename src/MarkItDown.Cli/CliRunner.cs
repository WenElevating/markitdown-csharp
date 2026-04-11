using MarkItDown.Core;

namespace MarkItDown.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = Parse(args);
            var engine = MarkItDownEngine.CreateDefault();
            var result = await engine.ConvertAsync(new DocumentConversionRequest(options.InputPath), cancellationToken);

            if (string.IsNullOrWhiteSpace(options.OutputPath))
            {
                await stdout.WriteAsync(result.Markdown);
                if (!result.Markdown.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                {
                    await stdout.WriteLineAsync();
                }
            }
            else
            {
                var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                await File.WriteAllTextAsync(options.OutputPath, result.Markdown, cancellationToken);
            }

            return 0;
        }
        catch (ConversionException ex) when (ex.Code is ConversionErrorCode.InvalidInput or ConversionErrorCode.UnsupportedFormat or ConversionErrorCode.UnsupportedContent)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 1;
        }
        catch (ConversionException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 2;
        }
    }

    private static CliOptions Parse(IReadOnlyList<string> args)
    {
        string? inputPath = null;
        string? outputPath = null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "-o":
                case "--output":
                    index++;
                    if (index >= args.Count)
                    {
                        throw new ConversionException(ConversionErrorCode.InvalidInput, "Missing output path after -o/--output.");
                    }

                    outputPath = args[index];
                    break;
                case "-h":
                case "--help":
                    throw new ConversionException(
                        ConversionErrorCode.InvalidInput,
                        "Usage: markitdown <input-file> [-o|--output <output-file>]");
                default:
                    if (inputPath is not null)
                    {
                        throw new ConversionException(
                            ConversionErrorCode.InvalidInput,
                            "Only one input file path is supported.");
                    }

                    inputPath = arg;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ConversionException(
                ConversionErrorCode.InvalidInput,
                "Usage: markitdown <input-file> [-o|--output <output-file>]");
        }

        return new CliOptions(inputPath, outputPath);
    }

    private sealed record CliOptions(string InputPath, string? OutputPath);
}
