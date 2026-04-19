<#
.SYNOPSIS
Runs dry-run and optional apply validation against a Supabase target database.

.DESCRIPTION
Links the repository to a staging or production Supabase project, prints the
migration list, performs `supabase db push --dry-run`, and optionally performs
the real `supabase db push`.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase2-validate.ps1 -EnvironmentName staging
#>

param(
    [ValidateSet("staging", "production")]
    [string]$EnvironmentName = "staging",

    [string]$ProjectRef,
    [string]$DbPassword,
    [string]$DbUrl,

    [switch]$Apply,
    [switch]$IncludeAll
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

$pushArgs = @(
    "db", "push", "--dry-run"
)

if (-not [string]::IsNullOrWhiteSpace($resolvedTarget.DbUrl)) {
    $pushArgs += @("--db-url", $resolvedTarget.DbUrl)
}
else {
    $pushArgs += @("--linked", "--password", $resolvedTarget.DbPassword)
}

if ($IncludeAll) {
    $pushArgs += "--include-all"
}

Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments $pushArgs

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry run completed. Re-run with -Apply to execute the migration push."
    return
}

$applyArgs = @(
    "db", "push"
)

if (-not [string]::IsNullOrWhiteSpace($resolvedTarget.DbUrl)) {
    $applyArgs += @("--db-url", $resolvedTarget.DbUrl)
}
else {
    $applyArgs += @("--linked", "--password", $resolvedTarget.DbPassword)
}

if ($IncludeAll) {
    $applyArgs += "--include-all"
}

Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments $applyArgs
Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @("migration", "list")

Write-Host ""
Write-Host "Supabase migration validation completed."
