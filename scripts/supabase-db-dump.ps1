param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [string]$OutputDirectory = ".\\out\\supabase-dumps",

    [ValidateSet("custom", "plain")]
    [string]$Format = "custom",

    [switch]$SchemaOnly
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

Require-Command "pg_dump"

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$extension = if ($Format -eq "plain") { "sql" } else { "dump" }
$mode = if ($SchemaOnly) { "schema" } else { "full" }
$outputPath = Join-Path $resolvedOutputDirectory "supabase-$mode-$timestamp.$extension"

$arguments = @(
    "--no-owner",
    "--no-privileges",
    "--format=$Format",
    "--file=$outputPath",
    $ConnectionString
)

if ($SchemaOnly) {
    $arguments = @("--schema-only") + $arguments
}

Write-Host "Creating Supabase dump at $outputPath"
& pg_dump @arguments

if ($LASTEXITCODE -ne 0) {
    throw "pg_dump failed with exit code $LASTEXITCODE."
}

Write-Host "Dump completed: $outputPath"
