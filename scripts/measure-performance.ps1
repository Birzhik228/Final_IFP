param(
    [string]$DotNet = 'dotnet',
    [ValidateRange(1, 3600)][int]$Seconds = 20,
    [string]$Concurrency = '1,10,25',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/performance')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../tests/LibraryLending.Tests.Performance/LibraryLending.Tests.Performance.csproj'
& $DotNet run --project $project --configuration Release -- --seconds $Seconds --concurrency $Concurrency --output $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw "Performance run failed. Inspect results.json in $OutputDirectory." }
