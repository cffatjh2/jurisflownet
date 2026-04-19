<#
.SYNOPSIS
Renders a concrete preview SQL file from the Phase 6 minimal onboarding template without executing it.

.DESCRIPTION
Loads `supabase/etl/sql/100_minimal_seed_and_onboarding.template.sql`, replaces placeholders with
the provided tenant/admin values, and writes a preview SQL file for manual review.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase6-onboarding-preview.ps1 -TenantSlug demo -TenantName "Demo Legal" -AdminEmail admin@example.com
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$TenantSlug,

    [Parameter(Mandatory = $true)]
    [string]$TenantName,

    [Parameter(Mandatory = $true)]
    [string]$AdminEmail,

    [string]$AdminName = "Founding Admin",

    [string]$Timezone = "Europe/Istanbul",

    [string]$AdminPasswordHash = "<replace-with-hash>",

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$templatePath = Join-Path $repoRoot "supabase\etl\sql\100_minimal_seed_and_onboarding.template.sql"

if (-not (Test-Path $templatePath)) {
    throw "Onboarding template was not found: $templatePath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "documentation\PHASE6_ONBOARDING_PREVIEW.sql"
}

$replacements = @{
    "{{TENANT_ID}}" = [guid]::NewGuid().ToString()
    "{{TENANT_SLUG}}" = $TenantSlug.Trim().ToLowerInvariant()
    "{{TENANT_NAME}}" = $TenantName.Trim()
    "{{FIRM_ENTITY_ID}}" = [guid]::NewGuid().ToString()
    "{{OFFICE_ID}}" = [guid]::NewGuid().ToString()
    "{{ADMIN_USER_ID}}" = [guid]::NewGuid().ToString()
    "{{STAFF_PROFILE_ID}}" = [guid]::NewGuid().ToString()
    "{{ADMIN_EMAIL}}" = $AdminEmail.Trim().ToLowerInvariant()
    "{{ADMIN_NAME}}" = $AdminName.Trim()
    "{{ADMIN_PASSWORD_HASH}}" = $AdminPasswordHash
    "{{TIMEZONE}}" = $Timezone.Trim()
}

$content = Get-Content -Path $templatePath -Raw
foreach ($entry in $replacements.GetEnumerator()) {
    $content = $content.Replace($entry.Key, $entry.Value)
}

$content | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host ("Phase 6 onboarding preview written to {0}" -f $OutputPath)
