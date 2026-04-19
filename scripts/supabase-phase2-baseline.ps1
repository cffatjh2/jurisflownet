<#
.SYNOPSIS
Pulls the current remote Supabase schema into a baseline migration file.

.DESCRIPTION
Links the repository to a staging or production Supabase project, lists current
migrations, runs `supabase db pull`, and then lists migration state again.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase2-baseline.ps1 -EnvironmentName staging
#>

param(
    [ValidateSet("staging", "production")]
    [string]$EnvironmentName = "staging",

    [string]$ProjectRef,
    [string]$DbPassword,
    [string]$DbUrl,

    [string]$MigrationName = "remote_schema_baseline",
    [string]$Schemas = "",
    [switch]$UsePgDelta
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "_supabase-cli.ps1")

$repoRoot = Get-RepositoryRootFromScript -ScriptRoot $PSScriptRoot
$supabaseRoot = Join-Path $repoRoot "supabase"

if (-not (Test-Path (Join-Path $supabaseRoot "config.toml"))) {
    throw "supabase/config.toml not found. Run 'npx supabase init --yes' first."
}

$resolvedTarget = Resolve-SupabaseTargetSettings `
    -EnvironmentName $EnvironmentName `
    -ProjectRef $ProjectRef `
    -DbPassword $DbPassword `
    -DbUrl $DbUrl

Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @("--version")

if (-not [string]::IsNullOrWhiteSpace($resolvedTarget.ProjectRef)) {
    Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @(
        "link",
        "--project-ref", $resolvedTarget.ProjectRef,
        "--password", $resolvedTarget.DbPassword,
        "--yes"
    )
}

Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @("migration", "list")

$pullArgs = @(
    "db", "pull", $MigrationName,
    "--yes"
)

if (-not [string]::IsNullOrWhiteSpace($resolvedTarget.DbUrl)) {
    $pullArgs += @("--db-url", $resolvedTarget.DbUrl)
}
else {
    $pullArgs += @("--linked", "--password", $resolvedTarget.DbPassword)
}

if (-not [string]::IsNullOrWhiteSpace($Schemas)) {
    $pullArgs += @("--schema", $Schemas)
}

if ($UsePgDelta) {
    $pullArgs += "--use-pg-delta"
}

Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments $pullArgs
Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @("migration", "list")

Write-Host ""
Write-Host "Supabase baseline pull completed."
Write-Host "Review the generated file under supabase/migrations and commit it before any new schema change."
