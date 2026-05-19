param(
    [string] $CliDir,
    [string] $OutputZip,
    [switch] $SkipInstall,
    [switch] $SkipTest,
    [switch] $SkipAudit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($CliDir)) {
    $CliDir = Join-Path $repoRoot "RustPlusDesktop\runtime\rustplus-cli"
}
else {
    $CliDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CliDir)
}

if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip = Join-Path $repoRoot "RustPlusDesktop\runtime\rustplus-cli.zip"
}
else {
    $OutputZip = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputZip)
}

if (-not (Test-Path -LiteralPath $CliDir -PathType Container)) {
    throw "CLI directory was not found: $CliDir"
}

$cmd = Get-Command npm.cmd -ErrorAction SilentlyContinue
if (-not $cmd) {
    $cmd = Get-Command npm -ErrorAction SilentlyContinue
}

if ($cmd) {
    $npm = $cmd.Source
}
else {
    $bundledNpm = Join-Path $repoRoot "RustPlusDesktop\runtime\node-win-x64\npm.cmd"
    if (-not (Test-Path -LiteralPath $bundledNpm -PathType Leaf)) {
        throw "npm was not found. Install Node.js (recommended) or restore RustPlusDesktop\runtime\node-win-x64."
    }
    $npm = $bundledNpm
}

function Invoke-Npm {
    param([Parameter(Mandatory=$true)][string[]] $Args)
    & $npm @Args
    if ($LASTEXITCODE -ne 0) {
        throw "npm $($Args -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $CliDir
try {
    if (-not $SkipInstall) {
        Invoke-Npm @("ci", "--omit=dev")
    }

    if (-not $SkipTest) {
        Invoke-Npm @("test")
    }

    if (-not $SkipAudit) {
        Invoke-Npm @("audit", "--omit=dev")
    }
}
finally {
    Pop-Location
}

$nodeModules = Join-Path $CliDir "node_modules"
if (-not (Test-Path -LiteralPath $nodeModules -PathType Container)) {
    throw "node_modules was not found. Run this script without -SkipInstall first."
}

$vendor = Join-Path $CliDir "vendor"
if (-not (Test-Path -LiteralPath $vendor -PathType Container)) {
    throw "vendor was not found. Expected: $vendor"
}

$packageJson = Join-Path $CliDir "package.json"
if (-not (Test-Path -LiteralPath $packageJson -PathType Leaf)) {
    throw "package.json was not found. Expected: $packageJson"
}

$packageLock = Join-Path $CliDir "package-lock.json"
if (-not (Test-Path -LiteralPath $packageLock -PathType Leaf)) {
    throw "package-lock.json was not found. Expected: $packageLock"
}

$patches = Join-Path $CliDir "PATCHES.md"
if (-not (Test-Path -LiteralPath $patches -PathType Leaf)) {
    throw "PATCHES.md was not found. Expected: $patches"
}

$outputDir = Split-Path -Parent $OutputZip
if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

if (Test-Path -LiteralPath $OutputZip -PathType Leaf) {
    Remove-Item -LiteralPath $OutputZip -Force
}

Push-Location $CliDir
try {
    Compress-Archive -Path @("node_modules", "vendor", "package.json", "package-lock.json", "PATCHES.md") -DestinationPath $OutputZip -CompressionLevel Optimal
}
finally {
    Pop-Location
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($OutputZip)
try {
    $sizeMb = [Math]::Round((Get-Item -LiteralPath $OutputZip).Length / 1MB, 2)
    Write-Host "Wrote $OutputZip"
    Write-Host "Entries: $($zip.Entries.Count)"
    Write-Host "Size: $sizeMb MB"
}
finally {
    $zip.Dispose()
}
