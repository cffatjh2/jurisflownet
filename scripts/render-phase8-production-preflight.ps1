<#
.SYNOPSIS
Builds a non-destructive preflight report for the future production cutover.

.DESCRIPTION
Validates that backup, restore, canonical build, ETL, and smoke artifacts exist for Phase 8.
This script does not call Supabase, Render, or any live environment.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase8-production-preflight.ps1
#>

param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "documentation\PHASE8_PREFLIGHT_REPORT.md"
}

$requiredFiles = @(
    "render.canonical.production.yaml",
    "documentation\RENDER_CANONICAL_PRODUCTION_ENV_MATRIX_TR.md",
    "documentation\SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md",
    "documentation\PHASE8_PRODUCTION_CUTOVER_CHECKLIST_TR.md",
    "documentation\PHASE8_ROLLBACK_CHECKLIST_TR.md",
    "documentation\PHASE7_PREFLIGHT_REPORT.md",
    "documentation\PHASE7_STAGING_SMOKE_PREVIEW.md",
    "scripts\render-phase8-production-smoke.ps1",
    "scripts\supabase-db-dump.ps1",
    "scripts\supabase-db-restore.ps1",
    "scripts\supabase-storage-backup.ps1",
    "scripts\supabase-storage-restore.ps1"
)

$expectedEnvKeys = @(
    "ConnectionStrings__DefaultConnection",
    "Storage__Supabase__Url",
    "Storage__Supabase__ServiceRoleKey",
    "Storage__Supabase__Bucket",
    "Jwt__Key",
    "Jwt__Issuer",
    "Jwt__Audience",
    "Tenancy__DefaultTenantSlug",
    "Tenancy__DefaultTenantName",
    "Seed__Enabled",
    "Security__DocumentEncryptionKey",
    "Security__DbEncryptionKey",
    "Security__AuditLogKey"
)

function Test-RelativeFile([string]$RelativePath) {
    $fullPath = Join-Path $repoRoot $RelativePath
    [pscustomobject]@{
        Path = $RelativePath
        Exists = (Test-Path -Path $fullPath)
    }
}

$fileChecks = @($requiredFiles | ForEach-Object { Test-RelativeFile $_ })
$programContent = Get-Content -Path (Join-Path $repoRoot "JurisFlow.Server\Program.cs") -Raw
$renderTemplate = Get-Content -Path (Join-Path $repoRoot "render.canonical.production.yaml") -Raw
$envMatrix = Get-Content -Path (Join-Path $repoRoot "documentation\RENDER_CANONICAL_PRODUCTION_ENV_MATRIX_TR.md") -Raw

$checks = @(
    [pscustomobject]@{ Name = "healthcheck_path"; Status = ($programContent -match 'MapHealthChecks\("/health"\)'); Detail = "/health maplenmis olmali" },
    [pscustomobject]@{ Name = "canonical_bootstrap_mode"; Status = ($renderTemplate -match 'Database__BootstrapMode' -and $renderTemplate -match 'value:\s*migrate'); Detail = "Canonical production blueprint migrate kullanmali" },
    [pscustomobject]@{ Name = "canonical_seed_disabled"; Status = ($renderTemplate -match 'Seed__Enabled' -and $renderTemplate -match 'value:\s*false'); Detail = "Production blueprint seed kapali olmali" },
    [pscustomobject]@{ Name = "canonical_healthcheck_path"; Status = ($renderTemplate -match 'healthCheckPath:\s*/health'); Detail = "Canonical production blueprint /health kullanmali" }
)

$missingEnvKeys = @()
foreach ($key in $expectedEnvKeys) {
    if ($envMatrix -notmatch [regex]::Escape($key)) {
        $missingEnvKeys += $key
    }
}

$content = @(
    "# Phase 8 Production Preflight Report",
    "",
    "- generated-at: $(Get-Date -Format s)",
    "- mode: repo-only preview",
    "- real-supabase-change: no",
    "- real-render-change: no",
    "",
    "## Core Checks",
    ""
)

foreach ($check in $checks) {
    $content += ("- `{0}` : {1} ({2})" -f $check.Name, ($(if ($check.Status) { "ok" } else { "missing" })), $check.Detail)
}

$content += @(
    "",
    "## Required Repo Files",
    ""
)

foreach ($item in $fileChecks) {
    $content += ("- `{0}` : {1}" -f $item.Path, ($(if ($item.Exists) { "ok" } else { "missing" })))
}

$content += @(
    "",
    "## Env Matrix Coverage",
    "",
    "- expected-env-key-count: $($expectedEnvKeys.Count)",
    "- missing-env-key-count: $($missingEnvKeys.Count)"
)

if ($missingEnvKeys.Count -eq 0) {
    $content += "- result: ok"
}
else {
    foreach ($key in $missingEnvKeys) {
        $content += "- missing-env-key: $key"
    }
}

$content += @(
    "",
    "## Verdict",
    ""
)

$hasMissing = @($fileChecks | Where-Object { -not $_.Exists }).Count -gt 0
$hasFailedChecks = @($checks | Where-Object { -not $_.Status }).Count -gt 0

if (-not $hasMissing -and -not $hasFailedChecks -and $missingEnvKeys.Count -eq 0) {
    $content += "- phase8-preflight: ready-for-future-cutover"
}
else {
    $content += "- phase8-preflight: blocked"
}

$content | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host ("Phase 8 preflight report written to {0}" -f $OutputPath)
