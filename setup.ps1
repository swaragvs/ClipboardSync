param(
    [string]$Configuration = "Release",
    [switch]$SelfContained,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "ClipboardSyncApp\ClipboardSyncApp.csproj"
$publishDir = Join-Path $root "publish"

function Ensure-DotNet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        Write-Host ".NET SDK not found. Installing .NET 8 SDK..." -ForegroundColor Yellow
        winget install --id Microsoft.DotNet.SDK.8 --source winget --accept-source-agreements --accept-package-agreements
        $env:Path += ";C:\Program Files\dotnet"
    }

    dotnet --version | Out-Null
}

Ensure-DotNet

Write-Host "Restoring NuGet packages..." -ForegroundColor Cyan
dotnet restore $project

Write-Host "Building project..." -ForegroundColor Cyan
dotnet build $project -c $Configuration --no-restore

Write-Host "Publishing app..." -ForegroundColor Cyan

if ($SelfContained) {
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $publishDir --no-build
} else {
    dotnet publish $project -c $Configuration -o $publishDir --no-build
}

Write-Host "Done. Output folder: $publishDir" -ForegroundColor Green
Write-Host "Run the app with: $publishDir\ClipboardSyncApp.exe" -ForegroundColor Green
