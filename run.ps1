param([int]$Port = 5180)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$sdkVersion = '8.0.428'
$sdkRoot = Join-Path $PSScriptRoot '.tools/dotnet'
$sdk = Join-Path $sdkRoot 'dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed) {
        $version = & $installed.Source --version 2>$null
        if ($LASTEXITCODE -eq 0 -and $version -like '8.0.*') { $sdk = $installed.Source }
    }
}
if (-not (Test-Path -LiteralPath $sdk)) {
    Write-Host "Installing the project's .NET SDK $sdkVersion locally..."
    New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null
    $installer = Join-Path $PSScriptRoot '.tools/dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version $sdkVersion -InstallDir $sdkRoot -NoPath
    if ($LASTEXITCODE -ne 0) { throw 'The local .NET SDK installation failed.' }
}
$env:DOTNET_ROOT = Split-Path -Parent $sdk
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
Write-Host "Opening BookBridge at http://localhost:$Port"
Write-Host 'Local demo: librarian@library.local / LibraryDemo1!'
& $sdk run --project src/LibraryLending.Web --no-launch-profile
exit $LASTEXITCODE
