<#
.SYNOPSIS
Creates a schema-only snapshot of a legacy Supabase database before a refactor sprint.

.DESCRIPTION
Resolves the target connection string from either parameters or environment variables,
then delegates to supabase-db-dump.ps1 with -SchemaOnly enabled.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-schema-snapshot.ps1 -EnvironmentName production
#>

param(
    [ValidateSet("staging", "production")]
    [string]$EnvironmentName = "production",

    [string]$ConnectionString,

    [string]$OutputDirectory = ".\\out\\supabase-schema-snapshots"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $envName = if ($EnvironmentName -eq "production") { "SUPABASE_PRODUCTION_DB_CONNECTION" } else { "SUPABASE_STAGING_DB_CONNECTION" }
    $ConnectionString = [Environment]::GetEnvironmentVariable($envName)
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Connection string is missing. Pass -ConnectionString or set the environment-specific SUPABASE_*_DB_CONNECTION variable."
}

$dumpScript = Join-Path $PSScriptRoot "supabase-db-dump.ps1"
if (-not (Test-Path $dumpScript)) {
    throw "Required script was not found: $dumpScript"
}

& $dumpScript `
    -ConnectionString $ConnectionString `
    -OutputDirectory $OutputDirectory `
    -Format plain `
    -SchemaOnly

if ($LASTEXITCODE -ne 0) {
    throw "Schema snapshot failed with exit code $LASTEXITCODE."
}
