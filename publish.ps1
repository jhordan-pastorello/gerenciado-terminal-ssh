param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SelfContained,
    [string]$Output
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $root "dist\StorageTerm"
}

$dotnet = $null
$command = Get-Command dotnet -ErrorAction SilentlyContinue
if ($command) { $dotnet = $command.Source }
if (-not $dotnet) {
    $fallback = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (Test-Path $fallback) { $dotnet = $fallback }
}
if (-not $dotnet) {
    throw "dotnet SDK 10 was not found. Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
}

if (Test-Path $Output) {
    Remove-Item $Output -Recurse -Force
}

$self = if ($SelfContained) { "true" } else { "false" }

Write-Host "Publishing StorageTerm ($Configuration, self-contained=$self)..."
& $dotnet publish (Join-Path $root "StorageTerm.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained $self `
    -o $Output `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# PDBs are not needed at runtime and the native terminal PDB is large.
Get-ChildItem $Output -Filter *.pdb -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Output "StorageTerm.exe"
if (-not (Test-Path $exe)) {
    throw "StorageTerm.exe was not produced in $Output."
}

$bytes = (Get-ChildItem $Output -File -Recurse | Measure-Object -Property Length -Sum).Sum
$size = [math]::Round($bytes / 1MB, 1)

Write-Host ""
Write-Host "OK: $exe"
Write-Host "Size: $size MB"
if ($SelfContained) {
    Write-Host "Self-contained: no .NET runtime required on the target PC."
} else {
    Write-Host "Framework-dependent: requires the .NET 10 Desktop Runtime."
}
