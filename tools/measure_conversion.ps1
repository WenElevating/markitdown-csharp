param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [int]$Iterations = 5
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\MarkItDown.Cli\MarkItDown.Cli.csproj'
$durations = [System.Collections.Generic.List[double]]::new()
for ($i = 0; $i -lt $Iterations; $i++) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & dotnet run --project $project --no-restore -- $InputPath --pipeline multimodal --vision off *> $null
    if ($LASTEXITCODE -ne 0) { throw "Conversion failed on iteration $($i + 1)." }
    $watch.Stop()
    $durations.Add($watch.Elapsed.TotalMilliseconds)
}
$ordered = $durations | Sort-Object
$p50 = $ordered[[Math]::Max(0, [Math]::Floor(($ordered.Count - 1) * 0.50))]
$p95 = $ordered[[Math]::Max(0, [Math]::Floor(($ordered.Count - 1) * 0.95))]
[pscustomobject]@{
    Input = (Resolve-Path $InputPath).Path
    Iterations = $Iterations
    MinMs = [Math]::Round($ordered[0], 2)
    P50Ms = [Math]::Round($p50, 2)
    P95Ms = [Math]::Round($p95, 2)
    MaxMs = [Math]::Round($ordered[-1], 2)
} | ConvertTo-Json
