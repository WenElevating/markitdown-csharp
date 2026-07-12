using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkItDown.Core;

public sealed record DoclingRequest(
    string RequestId,
    string ProtocolVersion,
    string Filename,
    string? MimeType,
    string ContentBase64);

public sealed record DoclingResponse(
    string RequestId,
    string ProtocolVersion,
    bool Success,
    string? DocumentJson = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IDoclingTransport
{
    Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default);
}

public static class DoclingProtocol
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(DoclingRequest request) => JsonSerializer.Serialize(request, Options);
    public static string Serialize(DoclingResponse response) => JsonSerializer.Serialize(response, Options);

    public static DoclingRequest DeserializeRequest(string json)
    {
        var request = JsonSerializer.Deserialize<DoclingRequest>(json, Options)
            ?? throw new InvalidDataException("Docling request was empty.");
        if (request.ProtocolVersion != "1" || string.IsNullOrWhiteSpace(request.RequestId))
            throw new InvalidDataException("Unsupported or missing Docling request protocol fields.");
        return request;
    }

    public static DoclingResponse DeserializeResponse(string json)
    {
        var response = JsonSerializer.Deserialize<DoclingResponse>(json, Options)
            ?? throw new InvalidDataException("Docling response was empty.");
        if (response.ProtocolVersion != "1" || string.IsNullOrWhiteSpace(response.RequestId))
            throw new InvalidDataException("Unsupported or missing Docling response protocol fields.");
        if (response.Success && string.IsNullOrWhiteSpace(response.DocumentJson))
            throw new InvalidDataException("Successful Docling response did not contain a document.");
        return response;
    }
}

public sealed class ProcessDoclingTransport : IDoclingTransport, IAsyncDisposable
{
    private readonly ProcessStartInfo _startInfo;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;

    public ProcessDoclingTransport(string executable = "python", string? workerScript = null, TimeSpan? requestTimeout = null)
    {
        _startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (!string.IsNullOrWhiteSpace(workerScript)) _startInfo.ArgumentList.Add(workerScript);
        _requestTimeout = requestTimeout ?? TimeSpan.FromMinutes(5);
    }

    public async Task<DoclingResponse> SendAsync(DoclingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await EnsureStartedAsync(cancellationToken);
                    await _input!.WriteLineAsync(DoclingProtocol.Serialize(request));
                    await _input.FlushAsync(cancellationToken);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(_requestTimeout);
                    var line = await _output!.ReadLineAsync(timeout.Token)
                        ?? throw new IOException("Docling worker exited without a response.");
                    var response = DoclingProtocol.DeserializeResponse(line);
                    if (!string.Equals(request.RequestId, response.RequestId, StringComparison.Ordinal))
                        throw new InvalidDataException("Docling response requestId did not match.");
                    return response;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await RestartAsync();
                    if (attempt == 1) throw new TimeoutException("Docling worker request timed out.");
                }
                catch (Exception) when (attempt == 0)
                {
                    await RestartAsync();
                }
            }
            throw new IOException("Docling worker failed after restart.");
        }
        finally { _gate.Release(); }
    }

    private Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false }) return Task.CompletedTask;
        _process?.Dispose();
        var process = new Process { StartInfo = _startInfo };
        if (!process.Start()) throw new InvalidOperationException("Unable to start Docling worker.");
        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;
        _ = DrainErrorsAsync(process.StandardError, cancellationToken);
        return Task.CompletedTask;
    }

    private static async Task DrainErrorsAsync(StreamReader error, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && await error.ReadLineAsync(cancellationToken) is not null) { }
    }

    private async Task RestartAsync()
    {
        await DisposeProcessAsync();
    }

    private async Task DisposeProcessAsync()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        catch { }
        _process.Dispose();
        _process = null;
        _input = null;
        _output = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await DisposeProcessAsync(); }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
