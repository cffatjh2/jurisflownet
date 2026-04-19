<#
.SYNOPSIS
Validates the canonical Supabase fresh-build migration set locally or against a linked remote target.

.DESCRIPTION
For `local`, runs `supabase db reset` to prove the canonical migrations can build an empty database.
For `staging` or `production`, links to the target project and performs `supabase db push --dry-run`
by default, with optional real apply when `-Apply` is specified.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase5-validate.ps1 -EnvironmentName local
#>

param(
    [ValidateSet("local", "staging", "production")]
    [string]$EnvironmentName = "local",

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

Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @("--version")

if ($EnvironmentName -eq "local") {
    $localArgs = @("db", "reset")
    if ($IncludeAll) {
        $localArgs += "--include-all"
    }

    Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments $localArgs
    Write-Host ""
    Write-Host "Local canonical fresh-build validation completed."
    return
}

$resolvedTarget = Resolve-SupabaseTargetSettings `
    -EnvironmentName $EnvironmentName `
    -ProjectRef $ProjectRef `
    -DbPassword $DbPassword `
    -DbUrl $DbUrl

if (-not [string]::IsNullOrWhiteSpace($resolvedTarget.ProjectRef)) {
    Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @(
        "link",
        "--project-ref", $resolvedTarget.ProjectRef,
        "--password", $resolvedTarget.DbPassword,
        "--yes"
    )
}

Invoke-SupabaseCli -WorkingDirectory $repoRoot -Arguments @("migration", "list")

$pushArgs = @("db", "push", "--dry-run")
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
    Write-Host "Remote dry run completed. Re-run with -Apply to execute the canonical push."
    return
}

$applyArgs = @("db", "push")
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
Write-Host "Canonical Supabase fresh-build validation completed."
