#Requires -Version 5.1
<#
.SYNOPSIS
    Alife Publish Script - Build and publish all projects
.DESCRIPTION
    Publishes Client, DeskPet, and all Function plugins to the shared distribution directory.
    Copies plugin files and syncs shared NuGet dependencies.
    Uses project defaults from Directory.Build.props (no overrides).
.PARAMETER OutputDir
    Output directory. Defaults to "$PSScriptRoot\..\Shared\Alife\Outputs".
.EXAMPLE
    .\Publish.ps1
    .\Publish.ps1 -OutputDir "C:\path\to\dist\Outputs"
#>

param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Src = Join-Path $Root "Sources"

if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "..\Shared\Alife\Outputs"
}

# Resolve to absolute path before any operations
$OutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
$PluginTarget = Join-Path (Split-Path $OutputDir -Parent) "Plugins"

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Publish Mode"                                  -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[Alife] Output:      $OutputDir"                       -ForegroundColor Cyan
Write-Host "[Alife] PluginTarget: $PluginTarget"                    -ForegroundColor Cyan
Write-Host ""

# ============================================================
# Step 0: Clean output directory
# ============================================================
Write-Host "[0/2] Cleaning output directory..." -ForegroundColor Yellow

if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Write-Host "  Cleaned: $OutputDir" -ForegroundColor Green
Write-Host ""

# ============================================================
# Step 1: Publish applications
# ============================================================
Write-Host "[1/2] Publish applications..." -ForegroundColor Yellow

Write-Host "  Publish Alife.Client..."
dotnet publish (Join-Path $Src "Alife\Alife.Client\Alife.Client.csproj") `
    -c Release -o (Join-Path $OutputDir "Alife.Client") -nologo --verbosity quiet

Write-Host "  Publish Alife.DeskPet.Client..."
dotnet publish (Join-Path $Src "Alife.DeskPet\Alife.DeskPet.Client\Alife.DeskPet.Client.csproj") `
    -c Release -o (Join-Path $OutputDir "Alife.DeskPet.Client") -nologo --verbosity quiet

$functionDirs = Get-ChildItem (Join-Path $Src "Alife.Function") -Directory | Where-Object { $_.Name -match '^Alife\.Function\.' }
foreach ($dir in $functionDirs) {
    $csproj = Join-Path $dir.FullName "$($dir.Name).csproj"
    $funcOut = Join-Path $OutputDir $dir.Name
    Write-Host "  Publish $($dir.Name)..."
    dotnet publish $csproj -c Release -o $funcOut -nologo --verbosity quiet
}

Write-Host ""

# ============================================================
# Step 2: Copy plugins
# ============================================================
Write-Host "[2/2] Copying plugins..." -ForegroundColor Yellow

if (Test-Path $PluginTarget) {
    Remove-Item $PluginTarget -Recurse -Force
}
New-Item -ItemType Directory -Path $PluginTarget -Force | Out-Null

foreach ($dir in $functionDirs) {
    $target = Join-Path $PluginTarget $dir.Name
    New-Item -ItemType Directory -Path $target -Force | Out-Null

    # Copy .cs files from source directory (including subdirectories)
    Get-ChildItem $dir.FullName -Filter "*.cs" -Recurse -File | Where-Object { $_.FullName -notmatch '\\obj\\' } | ForEach-Object {
        $relativePath = $_.FullName.Substring($dir.FullName.Length + 1)
        $destFile = Join-Path $target $relativePath
        $destDir = Split-Path $destFile -Parent
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }
        Copy-Item $_.FullName $destFile -Force
    }

    # Copy generated Razor .g.cs files (only if corresponding .razor exists)
    $generatedDir = Join-Path $dir.FullName "obj\Release\generated\Microsoft.CodeAnalysis.Razor.Compiler"
    if (Test-Path $generatedDir) {
        Get-ChildItem $generatedDir -Filter "*_razor.g.cs" -Recurse -File | ForEach-Object {
            $razorName = $_.Name -replace '_razor\.g\.cs$', ''
            $razorFile = Join-Path $dir.FullName "$razorName.razor"
            if (Test-Path $razorFile) {
                Copy-Item $_.FullName $target -Force
            } else {
                Write-Host "  [skip] $($_.Name)"
            }
        }
    }

    Write-Host "  [done] $($dir.Name)" -ForegroundColor Green
}
Write-Host ""

Write-Host ""
Write-Host "===================================================" -ForegroundColor Green
Write-Host "[Success] Publish complete!"                           -ForegroundColor Green
Write-Host "  Plugins: $PluginTarget"                               -ForegroundColor Green
Write-Host "===================================================" -ForegroundColor Green

Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
