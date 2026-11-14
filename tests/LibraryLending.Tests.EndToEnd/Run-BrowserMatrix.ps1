param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot 'TestResults/browser-matrix'),
    [string]$DotNet = 'dotnet',
    [switch]$InstallBrowsers
)

# Start a separately seeded Development server with an isolated database first.
# These workflows create library records and demo-member accounts.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'LibraryLending.Tests.EndToEnd.csproj'
& $DotNet build $project -p:UseAppHost=false
if ($LASTEXITCODE -ne 0) { throw 'Browser test build failed.' }
if ($InstallBrowsers) {
    & (Join-Path $PSScriptRoot 'bin/Debug/net8.0/playwright.ps1') install chromium firefox webkit
    if ($LASTEXITCODE -ne 0) { throw 'Playwright browser installation failed.' }
}

$results = (New-Item -ItemType Directory -Force -Path $ResultsDirectory).FullName
$env:LIBRARY_BASE_URL = $BaseUrl.TrimEnd('/')
$env:LIBRARY_A11Y_RESULTS = Join-Path $results 'accessibility'
$failed = @()
foreach ($browser in @('chromium', 'firefox', 'webkit')) {
    $env:LIBRARY_SCREENSHOTS = Join-Path $results "screenshots/$browser"
    & $DotNet test $project --no-build --logger "trx;LogFileName=$browser.trx" --results-directory (Join-Path $results $browser) -- "Playwright.BrowserName=$browser"
    if ($LASTEXITCODE -ne 0) { $failed += $browser }
}
if ($failed.Count -gt 0) { throw "Browser tests failed: $($failed -join ', '). See $results." }
Write-Output "All browser tests passed. TRX, axe-core JSON, and screenshots: $results"
