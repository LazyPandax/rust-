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

$bundledNpm = Join-Path $repoRoot "RustPlusDesktop\runtime\node-win-x64\npm.cmd"
if (Test-Path -LiteralPath $bundledNpm -PathType Leaf) {
    $npm = $bundledNpm
}
else {
    $cmd = Get-Command npm.cmd -ErrorAction SilentlyContinue
    if (-not $cmd) {
        $cmd = Get-Command npm -ErrorAction SilentlyContinue
    }
    if (-not $cmd) {
        throw "npm was not found. Install Node.js or restore RustPlusDesktop\runtime\node-win-x64."
    }
    $npm = $cmd.Source
}

Push-Location $CliDir
try {
    if (-not $SkipInstall) {
        & $npm ci --omit=dev
    }

    if (-not $SkipTest) {
        & $npm test
    }

    if (-not $SkipAudit) {
        & $npm audit --omit=dev
    }
}
finally {
    Pop-Location
}

$nodeModules = Join-Path $CliDir "node_modules"
if (-not (Test-Path -LiteralPath $nodeModules -PathType Container)) {
    throw "node_modules was not found. Run this script without -SkipInstall first."
}

$outputDir = Split-Path -Parent $OutputZip
if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

if (Test-Path -LiteralPath $OutputZip -PathType Leaf) {
    Remove-Item -LiteralPath $OutputZip -Force
}

Compress-Archive -Path $nodeModules -DestinationPath $OutputZip -CompressionLevel Optimal

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
