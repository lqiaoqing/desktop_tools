# 一键构建：restore -> build -> test（默认）；可选 -Publish 发布到 artifacts/
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Publish,
    [switch]$SkipRestore,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $root) { $root = (Get-Location).Path }

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Assert-Dotnet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw "dotnet not found. Install .NET 10 SDK: https://dotnet.microsoft.com/download"
    }
    $sdks = & dotnet --list-sdks
    if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
        Write-Host "Installed SDKs:" -ForegroundColor Yellow
        $sdks | ForEach-Object { Write-Host "  $_" }
        throw "Need .NET 10 SDK (global.json requires 10.0.110+)."
    }
}

Push-Location $root
try {
    Assert-Dotnet
    Write-Host "SDK: $((& dotnet --version))  Configuration: $Configuration"

    if (-not $SkipRestore) {
        Write-Step "Restore"
        & dotnet restore (Join-Path $root 'DesktopTools.sln')
        if ($LASTEXITCODE -ne 0) { throw "restore failed (exit=$LASTEXITCODE). See README NuGet FAQ." }
    }

    Write-Step "Build DesktopTools.sln"
    & dotnet build (Join-Path $root 'DesktopTools.sln') -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "build failed (exit=$LASTEXITCODE)" }

    $helperExe = Join-Path $root "src\DesktopTools\bin\$Configuration\net10.0-windows\helper\DesktopTools.Helper.exe"
    if (-not (Test-Path $helperExe)) {
        Write-Host "Helper missing at $helperExe, building Helper then main..." -ForegroundColor Yellow
        & dotnet build (Join-Path $root 'src\DesktopTools.Helper\DesktopTools.Helper.csproj') -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Helper build failed" }
        & dotnet build (Join-Path $root 'src\DesktopTools\DesktopTools.csproj') -c $Configuration --no-restore
        if ($LASTEXITCODE -ne 0) { throw "main build failed" }
    }
    if (-not (Test-Path $helperExe)) {
        throw "Helper not copied to $helperExe; cleanup will fail."
    }
    Write-Host "Helper OK: $helperExe" -ForegroundColor Green

    if (-not $SkipTests) {
        Write-Step "Test"
        & dotnet test (Join-Path $root 'tests\DesktopTools.Tests\DesktopTools.Tests.csproj') -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "test failed (exit=$LASTEXITCODE)" }
    }

    $exe = Join-Path $root "src\DesktopTools\bin\$Configuration\net10.0-windows\DesktopTools.exe"
    Write-Host ""
    Write-Host "Build succeeded." -ForegroundColor Green
    Write-Host "  App: $exe"

    if ($Publish) {
        Write-Step "Publish win-x64 self-contained"
        $out = Join-Path $root 'artifacts\win-x64'
        & dotnet publish (Join-Path $root 'src\DesktopTools.Helper\DesktopTools.Helper.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $out 'helper')
        if ($LASTEXITCODE -ne 0) { throw "publish Helper failed" }
        & dotnet publish (Join-Path $root 'src\DesktopTools\DesktopTools.csproj') -c Release -r win-x64 --self-contained true -o $out
        if ($LASTEXITCODE -ne 0) { throw "publish main failed" }
        if (-not (Test-Path (Join-Path $out 'helper\DesktopTools.Helper.exe'))) {
            throw "publish output missing helper\DesktopTools.Helper.exe"
        }
        Write-Host "  Publish dir: $out" -ForegroundColor Green
        Write-Host "  Run DesktopTools.exe on Windows 11 x64 without SDK."
    }
    exit 0
}
catch {
    Write-Host ""
    Write-Host "FAILED: $_" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}