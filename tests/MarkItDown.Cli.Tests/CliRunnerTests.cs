using System.Diagnostics;

namespace MarkItDown.Cli.Tests;

public sealed class CliRunnerTests
{
    [Fact]
    public async Task Cli_WritesMarkdownToStdout()
    {
        var result = await RunCliAsync(FixturePath.For("sample.html"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("## Experiment Setup", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public async Task Cli_WritesMarkdownToOutputFile()
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.md");

        try
        {
            var result = await RunCliAsync(FixturePath.For("sample.pdf"), "-o", outputFile);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(outputFile));
            var content = await File.ReadAllTextAsync(outputFile);
            Assert.Contains("Introduction", content);
            Assert.Equal(string.Empty, result.Stdout);
            Assert.Equal(string.Empty, result.Stderr);
        }
        finally
        {
            if (File.Exists(outputFile))
            {
                File.Delete(outputFile);
            }
        }
    }

    [Fact]
    public async Task Cli_ReturnsValidationErrorForUnsupportedInput()
    {
        var result = await RunCliAsync(FixturePath.For("unsupported.txt"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unsupported file format", result.Stderr);
    }

    [Fact]
    public async Task Cli_ReturnsValidationErrorForScannedPdf()
    {
        var result = await RunCliAsync(FixturePath.For("scanned.pdf"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Scanned or image-only PDFs are not supported", result.Stderr);
    }

    private static async Task<CliResult> RunCliAsync(params string[] args)
    {
        var projectPath = Path.Combine(FixturePath.RepositoryRoot, "src", "MarkItDown.Cli", "MarkItDown.Cli.csproj");
        var cliArguments = string.Join(" ", args.Select(Quote));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project {Quote(projectPath)} --no-build -- {cliArguments}",
                WorkingDirectory = FixturePath.RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.StartInfo.Environment["DOTNET_CLI_HOME"] = FixturePath.RepositoryRoot;
        process.StartInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        process.StartInfo.Environment["DOTNET_NOLOGO"] = "1";
        process.StartInfo.Environment["NUGET_PACKAGES"] = Path.Combine(FixturePath.RepositoryRoot, ".nuget", "packages");
        process.StartInfo.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(FixturePath.RepositoryRoot, ".nuget", "http-cache");
        process.StartInfo.Environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(FixturePath.RepositoryRoot, ".nuget", "plugins-cache");

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CliResult(process.ExitCode, stdout, stderr);
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private sealed record CliResult(int ExitCode, string Stdout, string Stderr);
}
