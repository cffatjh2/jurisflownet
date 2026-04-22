<#
.SYNOPSIS
Builds a non-destructive preflight report for the future canonical staging cutover.

.DESCRIPTION
Validates that the repository contains the canonical migration set, seed and ETL artifacts,
future Render blueprint, and staging env matrix needed for Phase 7. No Supabase or Render
operation is executed. The script only writes a Markdown report.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-canonical-preflight.ps1
#>

param(
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "documentation\PHASE7_PREFLIGHT_REPORT.md"
}

$requiredFiles = @(
    "render.canonical.staging.yaml",
    "documentation\RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md",
    "documentation\SUPABASE_PHASE5_FRESH_BUILD_RUNBOOK_TR.md",
    "documentation\SUPABASE_PHASE6_DATA_MIGRATION_RUNBOOK_TR.md",
    "documentation\PHASE7_STAGING_SMOKE_MANIFEST.json",
    "supabase\seed.sql",
    "scripts\supabase-phase5-validate.ps1",
    "scripts\supabase-phase6-preview.ps1",
    "scripts\supabase-phase6-onboarding-preview.ps1",
    "scripts\render-phase7-staging-smoke.ps1"
)

$requiredMigrationFiles = @(
    "supabase\migrations\20260419090000_001_foundation_tenants_identity.sql",
    "supabase\migrations\20260419091000_010_clients_matters_core.sql",
    "supabase\migrations\20260419092000_020_workflow_tasks_calendar.sql",
    "supabase\migrations\20260419093000_030_trust_accounting_core.sql",
    "supabase\migrations\20260419094000_031_trust_governance.sql",
    "supabase\migrations\20260419095000_040_billing_payments.sql",
    "supabase\migrations\20260419100000_050_documents_signatures_portal.sql",
    "supabase\migrations\20260419101000_060_integrations.sql",
    "supabase\migrations\20260419102000_070_audit_retention_views.sql"
)

$requiredEtlFiles = @(
    "supabase\etl\manifest.phase6.json",
    "supabase\etl\transforms.phase6.json",
    "supabase\etl\id-maps.phase6.json",
    "supabase\etl\verification.phase6.json",
    "supabase\etl\sql\001_phase6_bootstrap.sql",
    "supabase\etl\sql\090_verification.sql",
    "supabase\etl\sql\100_minimal_seed_and_onboarding.template.sql"
)

$expectedEnvKeys = @(
    "ConnectionStrings__DefaultConnection",
    "Database__ApplyMigrationsOnStartup",
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
    return [pscustomobject]@{
        Path = $RelativePath
        Exists = (Test-Path -Path $fullPath)
    }
}

$fileChecks = @($requiredFiles | ForEach-Object { Test-RelativeFile $_ })
$migrationChecks = @($requiredMigrationFiles | ForEach-Object { Test-RelativeFile $_ })
$etlChecks = @($requiredEtlFiles | ForEach-Object { Test-RelativeFile $_ })

$programContent = Get-Content -Path (Join-Path $repoRoot "JurisFlow.Server\Program.cs") -Raw
$renderTemplate = Get-Content -Path (Join-Path $repoRoot "render.canonical.staging.yaml") -Raw
$envMatrix = Get-Content -Path (Join-Path $repoRoot "documentation\RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md") -Raw
$manifest = Get-Content -Path (Join-Path $repoRoot "documentation\PHASE7_STAGING_SMOKE_MANIFEST.json") -Raw | ConvertFrom-Json

$checks = @(
    [pscustomobject]@{ Name = "healthcheck_path"; Status = ($programContent -match 'MapHealthChecks\("/health"\)'); Detail = "/health maplenmis olmali" },
    [pscustomobject]@{ Name = "tenant_middleware"; Status = ($programContent -match 'UseMiddleware<TenantResolutionMiddleware>'); Detail = "TenantResolutionMiddleware aktif olmali" },
    [pscustomobject]@{ Name = "canonical_bootstrap_mode"; Status = ($renderTemplate -match 'Database__BootstrapMode' -and $renderTemplate -match 'value:\s*migrate'); Detail = "Canonical staging blueprint migrate kullanmali" },
    [pscustomobject]@{ Name = "canonical_startup_migrations_disabled"; Status = ($renderTemplate -match 'Database__ApplyMigrationsOnStartup' -and $renderTemplate -match 'value:\s*false'); Detail = "Canonical staging blueprint deploy migration job modelini kullanmali" },
    [pscustomobject]@{ Name = "canonical_seed_disabled"; Status = ($renderTemplate -match 'Seed__Enabled' -and $renderTemplate -match 'value:\s*false'); Detail = "Canonical staging blueprint seed kapali olmali" },
    [pscustomobject]@{ Name = "canonical_healthcheck_path"; Status = ($renderTemplate -match 'healthCheckPath:\s*/health'); Detail = "Canonical staging blueprint /health kullanmali" },
    [pscustomobject]@{ Name = "smoke_manifest_present"; Status = ($manifest.surfaces.Count -ge 10); Detail = "Smoke manifest yeterli coverage icermeli" }
)

$missingEnvKeys = @()
foreach ($key in $expectedEnvKeys) {
    if ($envMatrix -notmatch [regex]::Escape($key)) {
        $missingEnvKeys += $key
    }
}

$content = @(
    "# Phase 7 Canonical Staging Preflight Report",
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
    "## Canonical Migration Set",
    ""
)

foreach ($item in $migrationChecks) {
    $content += ("- `{0}` : {1}" -f $item.Path, ($(if ($item.Exists) { "ok" } else { "missing" })))
}

$content += @(
    "",
    "## Phase 6 ETL/Seed Dependencies",
    ""
)

foreach ($item in $etlChecks) {
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
    "## Smoke Coverage",
    ""
)

foreach ($surface in $manifest.surfaces) {
    $content += ("- `{0}` : {1} {2} [{3}]" -f $surface.id, $surface.method, $surface.path, $surface.auth)
}

$content += @(
    "",
    "## Verdict",
    ""
)

$hasMissing = @($fileChecks + $migrationChecks + $etlChecks | Where-Object { -not $_.Exists }).Count -gt 0
$hasFailedChecks = @($checks | Where-Object { -not $_.Status }).Count -gt 0

if (-not $hasMissing -and -not $hasFailedChecks -and $missingEnvKeys.Count -eq 0) {
    $content += "- phase7-preflight: ready-for-future-cutover"
}
else {
    $content += "- phase7-preflight: blocked"
}

$content | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host ("Phase 7 preflight report written to {0}" -f $OutputPath)
