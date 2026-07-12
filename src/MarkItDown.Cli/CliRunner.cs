using System.CommandLine;
using System.Text.Json;
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

    private static readonly Option<string> BackendOption = new("--backend") { DefaultValueFactory = _ => "auto", Description = "Conversion backend: native, auto, or docling" };
    private static readonly Option<string> VisionOption = new("--vision") { DefaultValueFactory = _ => "auto", Description = "Visual enhancement: off, auto, or required" };
    private static readonly Option<string> DoclingModeOption = new("--docling-mode") { DefaultValueFactory = _ => "process", Description = "Docling transport: process or http" };
    private static readonly Option<string?> DoclingEndpointOption = new("--docling-endpoint") { Description = "Remote Docling HTTP endpoint" };
    private static readonly Option<string?> TimeoutOption = new("--timeout") { Description = "Conversion timeout (for example, 00:05:00)" };
    private static readonly Option<int?> MaxPagesOption = new("--max-pages") { Description = "Maximum pages to process" };
    private static readonly Option<long?> MaxBytesOption = new("--max-bytes") { Description = "Maximum input bytes" };
    private static readonly Option<string?> ReportOption = new("--diagnostics", "--report") { Description = "Write conversion diagnostics as JSON" };
    private static readonly Option<string?> AssetsOption = new("--assets") { Description = "Asset output directory" };
    private static readonly Option<bool> FailOnPartialOption = new("--fail-on-partial") { Description = "Return failure when fidelity is partial" };
    private static readonly Option<string> PipelineOption = new("--pipeline") { DefaultValueFactory = _ => "legacy", Description = "Pipeline: legacy or multimodal" };
    private static readonly Option<bool> AllowDocumentUploadOption = new("--allow-document-upload") { Description = "Allow configured visual providers to upload document pages" };

    public static RootCommand BuildCommand()
    {
        var root = new RootCommand("markitdown — Convert files and URLs to Markdown");

        root.Arguments.Add(InputArgument);
        root.Options.Add(OutputOption);
        root.Options.Add(ListFormatsOption);
        root.Options.Add(LlmKeyOption);
        root.Options.Add(LlmModelOption);
        root.Options.Add(LlmEndpointOption);
        root.Options.Add(BackendOption);
        root.Options.Add(VisionOption);
        root.Options.Add(DoclingModeOption);
        root.Options.Add(DoclingEndpointOption);
        root.Options.Add(TimeoutOption);
        root.Options.Add(MaxPagesOption);
        root.Options.Add(MaxBytesOption);
        root.Options.Add(ReportOption);
        root.Options.Add(AssetsOption);
        root.Options.Add(FailOnPartialOption);
        root.Options.Add(PipelineOption);
        root.Options.Add(AllowDocumentUploadOption);

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
            var context = BuildContext(parseResult);
            if (paths.Length == 1)
                return await ConvertSingleAsync(engine, llmClient, paths[0], outputPath, stdout, context, cancellationToken);

            return await ConvertMultipleAsync(engine, llmClient, paths, outputPath, stdout, stderr, context, cancellationToken);
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
            var context = BuildContext(parseResult);
            if (paths.Length == 1)
                return await ConvertSingleInvokeAsync(engine, llmClient, paths[0], outputPath, context);

            return await ConvertMultipleInvokeAsync(engine, llmClient, paths, outputPath, context);
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
        TextWriter stdout, ConversionContext context, CancellationToken ct)
    {
        using var publication = CreatePublication(outputPath, context, ct, out var effectiveContext);
        var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = publication?.AssetDirectory ?? ComputeAssetPath(inputPath, outputPath, effectiveContext), Options = effectiveContext.Options, Context = effectiveContext };
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

        await WriteDiagnosticsAsync(result, inputPath, outputPath, effectiveContext, ct);
        if (publication is not null)
        {
            publication.Complete();
            AssetPublication.CleanupOldPublications(publication);
        }
        return GetResultExitCode(result, effectiveContext);
    }

    private static async Task<int> ConvertSingleInvokeAsync(
        MarkItDownEngine engine, ILlmClient? llmClient, string inputPath, string? outputPath, ConversionContext context)
    {
        using var publication = CreatePublication(outputPath, context, CancellationToken.None, out var effectiveContext);
        var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = publication?.AssetDirectory ?? ComputeAssetPath(inputPath, outputPath, effectiveContext), Options = effectiveContext.Options, Context = effectiveContext };
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

        await WriteDiagnosticsAsync(result, inputPath, outputPath, effectiveContext, CancellationToken.None);
        if (publication is not null)
        {
            publication.Complete();
            AssetPublication.CleanupOldPublications(publication);
        }
        return GetResultExitCode(result, effectiveContext);
    }

    private static async Task<int> ConvertMultipleAsync(
        MarkItDownEngine engine, ILlmClient? llmClient, string[] inputPaths, string? outputPath,
        TextWriter stdout, TextWriter stderr, ConversionContext context, CancellationToken ct)
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
        var sawUnsupported = false;
        var sawPartial = false;
        var diagnosticEntries = new List<DiagnosticEntry>();

        foreach (var inputPath in inputPaths)
        {
            try
            {
                var outFile = FileSystemBoundary.BuildOutputFilePath(inputPath, outputPath!);
                using var publication = CreatePublication(outFile, context, ct, out var effectiveContext);

                var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = publication?.AssetDirectory ?? ComputeAssetPath(inputPath, outFile, effectiveContext), Options = effectiveContext.Options, Context = effectiveContext };
                var result = await engine.ConvertAsync(request, ct);

                await File.WriteAllTextAsync(outFile, result.Markdown, ct);
                if (publication is not null)
                {
                    publication.Complete();
                    AssetPublication.CleanupOldPublications(publication);
                }
                diagnosticEntries.Add(new DiagnosticEntry(inputPath, outFile, result.FidelityStatus.ToString(), result.Diagnostics ?? Array.Empty<ConversionDiagnostic>(), result.Usage));
                sawPartial |= result.FidelityStatus == FidelityStatus.Partial;

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
                sawUnsupported = true;
                diagnosticEntries.Add(new DiagnosticEntry(inputPath, null, FidelityStatus.Failed.ToString(),
                    [new ConversionDiagnostic("UNSUPPORTED_FORMAT", DiagnosticSeverity.Error, ex.Message)], new()));
            }
            catch (ConversionException ex)
            {
                await stderr.WriteLineAsync($"Error: {ex.Message}");
                exitCode = 2;
            }
        }
        await WriteBatchDiagnosticsAsync(diagnosticEntries, context, ct);
        if (exitCode == 0 && sawUnsupported) return 1;
        if (exitCode == 0 && sawPartial && context.Properties.TryGetValue("failOnPartial", out var fail) && fail is true) return 3;
        return exitCode;
    }

    private static async Task<int> ConvertMultipleInvokeAsync(
        MarkItDownEngine engine, ILlmClient? llmClient, string[] inputPaths, string? outputPath, ConversionContext context)
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
        var sawUnsupported = false;
        var sawPartial = false;
        var diagnosticEntries = new List<DiagnosticEntry>();

        foreach (var inputPath in inputPaths)
        {
            try
            {
                var outFile = FileSystemBoundary.BuildOutputFilePath(inputPath, outputPath!);
                using var publication = CreatePublication(outFile, context, CancellationToken.None, out var effectiveContext);

                var request = new DocumentConversionRequest { FilePath = inputPath, LlmClient = llmClient, AssetBasePath = publication?.AssetDirectory ?? ComputeAssetPath(inputPath, outFile, effectiveContext), Options = effectiveContext.Options, Context = effectiveContext };
                var result = await engine.ConvertAsync(request);

                await File.WriteAllTextAsync(outFile, result.Markdown);
                if (publication is not null)
                {
                    publication.Complete();
                    AssetPublication.CleanupOldPublications(publication);
                }
                diagnosticEntries.Add(new DiagnosticEntry(inputPath, outFile, result.FidelityStatus.ToString(), result.Diagnostics ?? Array.Empty<ConversionDiagnostic>(), result.Usage));
                sawPartial |= result.FidelityStatus == FidelityStatus.Partial;

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
                sawUnsupported = true;
                diagnosticEntries.Add(new DiagnosticEntry(inputPath, null, FidelityStatus.Failed.ToString(),
                    [new ConversionDiagnostic("UNSUPPORTED_FORMAT", DiagnosticSeverity.Error, ex.Message)], new()));
            }
            catch (ConversionException ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                exitCode = 2;
            }
        }
        await WriteBatchDiagnosticsAsync(diagnosticEntries, context, CancellationToken.None);
        if (exitCode == 0 && sawUnsupported) return 1;
        if (exitCode == 0 && sawPartial && context.Properties.TryGetValue("failOnPartial", out var fail) && fail is true) return 3;
        return exitCode;
    }

    private static string? ComputeAssetPath(string inputPath, string? outputPath, ConversionContext? context = null)
    {
        if (context?.Properties.TryGetValue("assetsPath", out var configured) == true && configured is string configuredPath && !string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;
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

    private static AssetPublicationLease? CreatePublication(
        string? outputPath,
        ConversionContext context,
        CancellationToken cancellationToken,
        out ConversionContext effectiveContext)
    {
        effectiveContext = context;
        if (context.Pipeline != PipelineMode.Multimodal || string.IsNullOrWhiteSpace(outputPath)) return null;
        if (context.Properties.TryGetValue("assetsPath", out var configured) && configured is string configuredPath && !string.IsNullOrWhiteSpace(configuredPath))
            return null;
        var fullOutput = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutput) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(fullOutput);
        var root = Path.Combine(directory, stem + "_files");
        var lease = AssetPublication.Acquire(root, fullOutput, cancellationToken);
        var properties = new Dictionary<string, object?>(context.Properties)
        {
            ["assetPathPrefix"] = Path.GetRelativePath(directory, lease.AssetDirectory).Replace('\\', '/')
        };
        effectiveContext = context with { Properties = properties };
        return lease;
    }

    private static string BuildOutputFilePath(string inputPath, string outputPath)
    {
        return Path.Combine(outputPath, Path.GetFileNameWithoutExtension(inputPath) + ".md");
    }

    internal static string? FindDuplicateOutput(IEnumerable<string> inputPaths, string outputPath)
    {
        return FileSystemBoundary.FindDuplicateOutput(inputPaths, outputPath);
    }

    private static ConversionContext BuildContext(ParseResult parseResult)
    {
        var backendText = parseResult.GetValue(BackendOption) ?? "auto";
        var visionText = parseResult.GetValue(VisionOption) ?? "auto";
        if (!Enum.TryParse<ConversionBackendMode>(backendText, ignoreCase: true, out var backend))
            throw new ConversionException($"Unknown backend '{backendText}'. Expected native, auto, or docling.");
        if (!Enum.TryParse<VisionMode>(visionText, ignoreCase: true, out var vision))
            throw new ConversionException($"Unknown vision mode '{visionText}'. Expected off, auto, or required.");
        var pipelineText = parseResult.GetValue(PipelineOption) ?? "legacy";
        if (!Enum.TryParse<PipelineMode>(pipelineText, ignoreCase: true, out var pipeline))
            throw new ConversionException($"Unknown pipeline '{pipelineText}'. Expected legacy or multimodal.");
        var explicitVision = parseResult.Tokens.Any(t => t.Value is "--vision");
        var explicitUpload = parseResult.Tokens.Any(t => t.Value is "--allow-document-upload");
        var explicitPartial = parseResult.Tokens.Any(t => t.Value is "--fail-on-partial");
        var explicitDiagnostics = parseResult.Tokens.Any(t => t.Value is "--diagnostics" or "--report");
        var explicitPipeline = parseResult.Tokens.Any(t => t.Value is "--pipeline");
        if (backend == ConversionBackendMode.Docling && !explicitPipeline)
            pipeline = PipelineMode.Multimodal;
        if (backend == ConversionBackendMode.Docling && explicitPipeline && pipeline != PipelineMode.Multimodal)
            throw new ConversionException("--backend docling requires --pipeline multimodal.");
        if (pipeline == PipelineMode.Legacy && (explicitVision || explicitUpload || explicitPartial || explicitDiagnostics))
            throw new ConversionException("--vision, --allow-document-upload, --fail-on-partial, and --diagnostics require --pipeline multimodal.");
        var allowUpload = parseResult.GetValue(AllowDocumentUploadOption);
        if (pipeline == PipelineMode.Multimodal && vision == VisionMode.Off && allowUpload)
            throw new ConversionException("--allow-document-upload cannot be used with --vision off.");

        var timeout = TimeSpan.FromMinutes(5);
        var timeoutText = parseResult.GetValue(TimeoutOption);
        if (!string.IsNullOrWhiteSpace(timeoutText) && (!TimeSpan.TryParse(timeoutText, out timeout) || timeout <= TimeSpan.Zero))
            throw new ConversionException($"Invalid timeout '{timeoutText}'.");

        var properties = new Dictionary<string, object?>
        {
            ["doclingMode"] = parseResult.GetValue(DoclingModeOption),
            ["doclingEndpoint"] = parseResult.GetValue(DoclingEndpointOption),
            ["reportPath"] = parseResult.GetValue(ReportOption),
            ["assetsPath"] = parseResult.GetValue(AssetsOption),
            ["failOnPartial"] = parseResult.GetValue(FailOnPartialOption)
        };
        var doclingMode = parseResult.GetValue(DoclingModeOption) ?? "process";
        IDoclingTransport? doclingTransport = null;
        if (pipeline == PipelineMode.Multimodal && vision != VisionMode.Off)
        {
            if (doclingMode.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                var endpoint = parseResult.GetValue(DoclingEndpointOption);
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                    throw new ConversionException("--docling-endpoint must be an absolute HTTP(S) URI when --docling-mode http is used.");
                doclingTransport = new HttpDoclingTransport(uri);
            }
            else if (doclingMode.Equals("process", StringComparison.OrdinalIgnoreCase))
            {
                var worker = Environment.GetEnvironmentVariable("MARKITDOWN_DOCLING_WORKER")
                    ?? (File.Exists(Path.Combine(AppContext.BaseDirectory, "docling_worker.py"))
                        ? Path.Combine(AppContext.BaseDirectory, "docling_worker.py")
                        : Path.Combine(Environment.CurrentDirectory, "tools", "docling_worker.py"));
                if (File.Exists(worker)) doclingTransport = new ProcessDoclingTransport("python", worker);
            }
            else
            {
                throw new ConversionException($"Unknown Docling mode '{doclingMode}'. Expected process or http.");
            }
        }
        return new ConversionContext
        {
            Backend = backend,
            Vision = vision,
            Timeout = timeout,
            MaxPages = parseResult.GetValue(MaxPagesOption),
            MaxBytes = parseResult.GetValue(MaxBytesOption),
            Properties = properties,
            Pipeline = pipeline,
            Options = new ConversionOptions
            {
                PipelineMode = pipeline,
                VisionMode = pipeline == PipelineMode.Multimodal ? vision : null,
                Privacy = new PrivacyOptions { AllowDocumentUpload = allowUpload },
                DoclingTransport = doclingTransport
            }
        };
    }

    private static int GetResultExitCode(DocumentConversionResult result, ConversionContext context)
    {
        var failOnPartial = context.Properties.TryGetValue("failOnPartial", out var value) && value is true;
        return failOnPartial && result.FidelityStatus == FidelityStatus.Partial ? 3 : 0;
    }

    private static async Task WriteDiagnosticsAsync(
        DocumentConversionResult result,
        string inputPath,
        string? outputPath,
        ConversionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Properties.TryGetValue("reportPath", out var configured) || configured is not string reportPath || string.IsNullOrWhiteSpace(reportPath))
            return;
        var fullReport = Path.GetFullPath(reportPath);
        if (!string.IsNullOrWhiteSpace(outputPath) &&
            string.Equals(fullReport, Path.GetFullPath(outputPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ConversionException("Diagnostics path cannot be the same as the Markdown output path.");
        await WriteBatchDiagnosticsAsync([new DiagnosticEntry(
            inputPath,
            outputPath,
            result.FidelityStatus.ToString(),
            result.Diagnostics ?? Array.Empty<ConversionDiagnostic>(),
            result.Usage)], context, cancellationToken, outputPath);
    }

    private static async Task WriteBatchDiagnosticsAsync(
        IReadOnlyList<DiagnosticEntry> entries,
        ConversionContext context,
        CancellationToken cancellationToken,
        string? markdownOutputPath = null)
    {
        if (!context.Properties.TryGetValue("reportPath", out var configured) || configured is not string reportPath || string.IsNullOrWhiteSpace(reportPath))
            return;
        var fullReport = Path.GetFullPath(reportPath);
        if (!string.IsNullOrWhiteSpace(markdownOutputPath) &&
            string.Equals(fullReport, Path.GetFullPath(markdownOutputPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ConversionException("Diagnostics path cannot be the same as the Markdown output path.");
        var document = new DiagnosticDocument("1", entries);
        var directory = Path.GetDirectoryName(fullReport);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = fullReport + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            File.Move(temp, fullReport, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
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
        await writer.WriteLineAsync("  --backend        native, auto, or docling (default: auto)");
        await writer.WriteLineAsync("  --vision         off, auto, or required (default: auto)");
        await writer.WriteLineAsync("  --docling-mode   process or http (default: process)");
        await writer.WriteLineAsync("  --docling-endpoint Remote Docling HTTP endpoint");
        await writer.WriteLineAsync("  --timeout        Conversion timeout");
        await writer.WriteLineAsync("  --max-pages      Maximum pages");
        await writer.WriteLineAsync("  --max-bytes      Maximum input bytes");
        await writer.WriteLineAsync("  --report         Diagnostics JSON output");
        await writer.WriteLineAsync("  --assets         Asset output directory");
        await writer.WriteLineAsync("  --fail-on-partial Fail on partial fidelity");
        await writer.WriteLineAsync("  --pipeline      legacy or multimodal (default: legacy)");
        await writer.WriteLineAsync("  --allow-document-upload Allow document page upload");
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

internal sealed record DiagnosticDocument(string SchemaVersion, IReadOnlyList<DiagnosticEntry> Entries);
internal sealed record DiagnosticEntry(
    string Input,
    string? Output,
    string Status,
    IReadOnlyList<ConversionDiagnostic> Diagnostics,
    ConversionUsage Usage);
