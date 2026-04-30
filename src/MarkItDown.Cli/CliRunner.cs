using System.CommandLine;
using MarkItDown.Core;
using MarkItDown.Converters.Html;
using MarkItDown.Converters.Pdf;
using MarkItDown.Converters.Office;
using MarkItDown.Converters.Data;
using MarkItDown.Converters.Media;
using MarkItDown.Converters.Web;
using MarkItDown.Llm;

namespace MarkItDown.Cli;

public static class CliRunner
{
    private static readonly Argument<string[]> InputArgument = new("paths")
    {
        Description = "Input file paths or URLs to convert",
        Arity = ArgumentArity.ZeroOrMore
    };

    private static readonly Option<string?> OutputOption = new("-o", "--output")
    {
        Description = "Output file or directory (default: stdout)"
    };

    private static readonly Option<bool> ListFormatsOption = new("--list-formats")
    {
        Description = "List all supported formats"
    };

    private static readonly Option<string?> LlmKeyOption = new("--llm-key")
    {
        Description = "OpenAI API key (enables LLM captioning for images)"
    };

    private static readonly Option<string?> LlmModelOption = new("--llm-model")
    {
        Description = "LLM model name (default: gpt-4o)"
    };

    private static readonly Option<string?> LlmEndpointOption = new("--llm-endpoint")
    {
        Description = "Custom API endpoint (e.g. Azure OpenAI)"
    };

    public static RootCommand BuildCommand()
    {
        var root = new RootCommand("markitdown — Convert files and URLs to Markdown");

        root.Arguments.Add(InputArgument);
        root.Options.Add(OutputOption);
        root.Options.Add(ListFormatsOption);
        root.Options.Add(LlmKeyOption);
        root.Options.Add(LlmModelOption);
        root.Options.Add(LlmEndpointOption);

        root.SetAction(parseResult =>
        {
            return RunInvokeAsync(parseResult).GetAwaiter().GetResult();
        });

        return root;
    }

    /// <summary>
    /// Legacy entry point for backward compatibility (used by tests).
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        var root = BuildCommand();
        var parseResult = root.Parse(args);

        // Handle --help
        if (parseResult.Errors.Count == 0 && parseResult.Tokens.Any(t => t.Value is "-h" or "--help"))
        {
            await WriteHelpAsync(root, stdout);
            return 0;
        }

        // Handle parse errors
        if (parseResult.Errors.Count > 0)
        {
            foreach (var error in parseResult.Errors)
                await stderr.WriteLineAsync(error.Message);
            return 1;
        }

        // Handle --list-formats
        if (parseResult.GetValue(ListFormatsOption))
        {
            foreach (var line in GetSupportedFormats())
                await stdout.WriteLineAsync(line);
            return 0;
        }

        var paths = parseResult.GetValue(InputArgument);
        if (paths is null || paths.Length == 0)
        {
            await WriteHelpAsync(root, stdout);
            return 0;
        }

        var outputPath = parseResult.GetValue(OutputOption);
        var llmKey = parseResult.GetValue(LlmKeyOption);
        var llmModel = parseResult.GetValue(LlmModelOption) ?? "gpt-4o";
        var llmEndpoint = parseResult.GetValue(LlmEndpointOption);

        var engine = BuildEngine();
        var llmClient = BuildLlmClient(llmKey, llmModel, llmEndpoint);

        try
        {
            if (paths.Length == 1)
                return await ConvertSingleAsync(engine, llmClient, paths[0], outputPath, stdout, cancellationToken);

            return await ConvertMultipleAsync(engine, llmClient, paths, outputPath, stdout, stderr, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            await stderr.WriteLineAsync(ex.Message);
            return 1;
        }
        catch (UnsupportedFormatException ex)
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

    private static async Task<int> RunInvokeAsync(ParseResult parseResult)
    {
        // Handle --list-formats
        if (parseResult.GetValue(ListFormatsOption))
        {
            foreach (var line in GetSupportedFormats())
                Console.WriteLine(line);
            return 0;
        }

        var paths = parseResult.GetValue(InputArgument);
        if (paths is null || paths.Length == 0)
            return 0; // Let System.CommandLine show help

        var outputPath = parseResult.GetValue(OutputOption);
        var llmKey = parseResult.GetValue(LlmKeyOption);
        var llmModel = parseResult.GetValue(LlmModelOption) ?? "gpt-4o";
        var llmEndpoint = parseResult.GetValue(LlmEndpointOption);

        var engine = BuildEngine();
        var llmClient = BuildLlmClient(llmKey, llmModel, llmEndpoint);

        try
        {
            if (paths.Length == 1)
                return await ConvertSingleInvokeAsync(engine, llmClient, paths[0], outputPath);

            return await ConvertMultipleInvokeAsync(engine, llmClient, paths, outputPath);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (UnsupportedFormatException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (ConversionException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static MarkItDownEngine BuildEngine() => new(builder => builder
        .Add(new HtmlConverter())
        .Add(new PdfConverter())
        .Add(new CsvConverter())
        .Add(new XlsxConverter())
        .Add(new DocxConverter())
        .Add(new PptxConverter())
        .Add(new MsgConverter())
        .Add(new MarkdownConverter())
        .Add(new JsonConverter())
        .Add(new JsonlConverter())
        .Add(new XmlConverter())
        .Add(new IpynbConverter())
        .Add(new RssConverter())
        .Add(new EpubConverter())
        .Add(new ZipConverter())
        .Add(new ImageConverter())
        .Add(new AudioConverter())
        .Add(new WikipediaConverter())
        .Add(new WebConverter()));

    private static ILlmClient? BuildLlmClient(string? apiKey, string model, string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        return new OpenAILlmClient(new LlmClientOptions
        {
            ApiKey = apiKey,
            Model = model,
            Endpoint = endpoint
        });
    }

    private static async Task<int> ConvertSingleAsync(
        MarkItDownEngine engine, ILlmClient? llmClient, string inputPath, string? outputPath,
        TextWriter stdout, CancellationToken ct)
    {
        var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = ComputeAssetPath(inputPath, outputPath) };
        var result = await engine.ConvertAsync(request, ct);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            await stdout.WriteAsync(result.Markdown);
            if (!result.Markdown.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                await stdout.WriteLineAsync();
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outputPath, result.Markdown, ct);
            await stdout.WriteLineAsync($"Converted: {inputPath} -> {Path.GetFullPath(outputPath)}");
        }

        if (!string.IsNullOrWhiteSpace(result.AssetDirectory))
        {
            await stdout.WriteLineAsync($"Images saved to: {Path.GetFullPath(result.AssetDirectory)}");
        }

        return 0;
    }

    private static async Task<int> ConvertSingleInvokeAsync(
        MarkItDownEngine engine, ILlmClient? llmClient, string inputPath, string? outputPath)
    {
        var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = ComputeAssetPath(inputPath, outputPath) };
        var result = await engine.ConvertAsync(request);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Write(result.Markdown);
            if (!result.Markdown.EndsWith(Environment.NewLine, StringComparison.Ordinal))
                Console.WriteLine();
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outputPath, result.Markdown);
            Console.WriteLine($"Converted: {inputPath} -> {Path.GetFullPath(outputPath)}");
        }

        if (!string.IsNullOrWhiteSpace(result.AssetDirectory))
        {
            Console.WriteLine($"Images saved to: {Path.GetFullPath(result.AssetDirectory)}");
        }

        return 0;
    }

    private static async Task<int> ConvertMultipleAsync(
        MarkItDownEngine engine, ILlmClient? llmClient, string[] inputPaths, string? outputPath,
        TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            await stderr.WriteLineAsync("Multiple input files require --output to specify an output directory.");
            return 1;
        }

        var duplicateOutput = FindDuplicateOutput(inputPaths, outputPath!);
        if (duplicateOutput is not null)
        {
            await stderr.WriteLineAsync($"Multiple input files would write to the same output file: {duplicateOutput}");
            return 1;
        }

        Directory.CreateDirectory(outputPath!);
        var exitCode = 0;

        foreach (var inputPath in inputPaths)
        {
            try
            {
                var outFile = FileSystemBoundary.BuildOutputFilePath(inputPath, outputPath!);

                var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = ComputeAssetPath(inputPath, outFile) };
                var result = await engine.ConvertAsync(request, ct);

                await File.WriteAllTextAsync(outFile, result.Markdown, ct);

                await stdout.WriteLineAsync($"Converted: {inputPath} -> {outFile}");
            }
            catch (FileNotFoundException ex)
            {
                await stderr.WriteLineAsync($"Error: {ex.Message}");
                exitCode = 1;
            }
            catch (UnsupportedFormatException ex)
            {
                await stderr.WriteLineAsync($"Skipped: {ex.Message}");
            }
            catch (ConversionException ex)
            {
                await stderr.WriteLineAsync($"Error: {ex.Message}");
                exitCode = 2;
            }
        }

        return exitCode;
    }

    private static async Task<int> ConvertMultipleInvokeAsync(
        MarkItDownEngine engine, ILlmClient? llmClient, string[] inputPaths, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Multiple input files require --output to specify an output directory.");
            return 1;
        }

        var duplicateOutput = FindDuplicateOutput(inputPaths, outputPath!);
        if (duplicateOutput is not null)
        {
            Console.Error.WriteLine($"Multiple input files would write to the same output file: {duplicateOutput}");
            return 1;
        }

        Directory.CreateDirectory(outputPath!);
        var exitCode = 0;

        foreach (var inputPath in inputPaths)
        {
            try
            {
                var outFile = FileSystemBoundary.BuildOutputFilePath(inputPath, outputPath!);

                var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = ComputeAssetPath(inputPath, outFile) };
                var result = await engine.ConvertAsync(request);

                await File.WriteAllTextAsync(outFile, result.Markdown);

                Console.WriteLine($"Converted: {inputPath} -> {outFile}");
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                exitCode = 1;
            }
            catch (UnsupportedFormatException ex)
            {
                Console.Error.WriteLine($"Skipped: {ex.Message}");
            }
            catch (ConversionException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                exitCode = 2;
            }
        }

        return exitCode;
    }

    private static string? ComputeAssetPath(string inputPath, string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            var stem = Path.GetFileNameWithoutExtension(outputPath);
            return Path.Combine(dir ?? ".", stem + "_files");
        }

        var inputDir = Path.GetDirectoryName(Path.GetFullPath(inputPath));
        var inputStem = Path.GetFileNameWithoutExtension(inputPath);
        return Path.Combine(inputDir ?? ".", inputStem + "_files");
    }

    private static string BuildOutputFilePath(string inputPath, string outputPath)
    {
        return Path.Combine(outputPath, Path.GetFileNameWithoutExtension(inputPath) + ".md");
    }

    internal static string? FindDuplicateOutput(IEnumerable<string> inputPaths, string outputPath)
    {
        return FileSystemBoundary.FindDuplicateOutput(inputPaths, outputPath);
    }

    private static string[] GetSupportedFormats() =>
    [
        "Supported formats:",
        "",
        "Documents:  .docx, .pptx, .xlsx, .csv, .msg, .pdf, .html, .htm",
        "Data:       .json, .jsonl, .xml, .rss, .atom, .ipynb, .epub, .zip, .md, .markdown",
        "Media:      .jpg, .jpeg, .png, .mp3, .wav, .m4a",
        "Web:        URLs (http://, https://), Wikipedia articles"
    ];

    private static async Task WriteHelpAsync(RootCommand root, TextWriter writer)
    {
        await writer.WriteLineAsync("markitdown — Convert files and URLs to Markdown");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("Usage: markitdown <path> [<path>...] [options]");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("Arguments:");
        await writer.WriteLineAsync("  <paths>        Input file paths or URLs to convert");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("Options:");
        await writer.WriteLineAsync("  -o, --output     Output file or directory (default: stdout)");
        await writer.WriteLineAsync("  --list-formats   List all supported formats");
        await writer.WriteLineAsync("  --llm-key        OpenAI API key (enables LLM captioning)");
        await writer.WriteLineAsync("  --llm-model      LLM model name (default: gpt-4o)");
        await writer.WriteLineAsync("  --llm-endpoint   Custom API endpoint (e.g. Azure OpenAI)");
        await writer.WriteLineAsync("  -h, --help       Show this help message");
        await writer.WriteLineAsync("  -V, --version    Show version number");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("Supported formats:");
        await writer.WriteLineAsync("  Documents:  .docx, .pptx, .xlsx, .csv, .msg, .pdf, .html, .htm");
        await writer.WriteLineAsync("  Data:       .json, .jsonl, .xml, .rss, .atom, .ipynb, .epub, .zip, .md");
        await writer.WriteLineAsync("  Media:      .jpg, .jpeg, .png, .mp3, .wav, .m4a");
        await writer.WriteLineAsync("  Web:        URLs (http/https), Wikipedia articles");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync("Examples:");
        await writer.WriteLineAsync("  markitdown document.docx");
        await writer.WriteLineAsync("  markitdown data.json -o output.md");
        await writer.WriteLineAsync("  markitdown *.pdf -o output/");
        await writer.WriteLineAsync("  markitdown https://example.com");
        await writer.WriteLineAsync("  markitdown photo.jpg --llm-key sk-...");
    }
}
