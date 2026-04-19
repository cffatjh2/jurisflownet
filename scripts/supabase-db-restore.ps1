param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [switch]$Apply,

    [string]$PreviewDirectory = ".\\out\\supabase-restore-preview"
)

$ErrorActionPreference = "Stop"

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

$resolvedInputPath = [System.IO.Path]::GetFullPath($InputPath)
if (-not (Test-Path -LiteralPath $resolvedInputPath)) {
    throw "InputPath was not found: $resolvedInputPath"
}

$extension = [System.IO.Path]::GetExtension($resolvedInputPath).ToLowerInvariant()

if (-not $Apply) {
    Require-Command "pg_restore"
    $resolvedPreviewDirectory = [System.IO.Path]::GetFullPath($PreviewDirectory)
    New-Item -ItemType Directory -Force -Path $resolvedPreviewDirectory | Out-Null

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $listPath = Join-Path $resolvedPreviewDirectory "restore-$timestamp.list.txt"

    if ($extension -eq ".sql") {
        Copy-Item -LiteralPath $resolvedInputPath -Destination (Join-Path $resolvedPreviewDirectory "restore-$timestamp.preview.sql") -Force
        Write-Host "Dry run complete. Plain SQL backup detected; review copied script in $resolvedPreviewDirectory"
        exit 0
    }

    & pg_restore "--list" "--file=$listPath" $resolvedInputPath

    if ($LASTEXITCODE -ne 0) {
        throw "pg_restore --list failed with exit code $LASTEXITCODE."
    }

    Write-Host "Dry run complete. Restore inventory written to $listPath"
    exit 0
}

if ($extension -eq ".sql") {
    Require-Command "psql"
    Write-Host "Applying plain SQL restore from $resolvedInputPath"
    Get-Content -LiteralPath $resolvedInputPath -Raw | & psql $ConnectionString
    if ($LASTEXITCODE -ne 0) {
        throw "psql restore failed with exit code $LASTEXITCODE."
    }
    exit 0
}

Require-Command "pg_restore"
Write-Host "Applying custom-format restore from $resolvedInputPath"
& pg_restore `
    "--clean" `
    "--if-exists" `
    "--no-owner" `
    "--no-privileges" `
    "--dbname=$ConnectionString" `
    $resolvedInputPath

if ($LASTEXITCODE -ne 0) {
    throw "pg_restore apply failed with exit code $LASTEXITCODE."
}
