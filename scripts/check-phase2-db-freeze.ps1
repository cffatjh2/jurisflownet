<#
.SYNOPSIS
Fails if a change set introduces database migration files during the Phase 2 legacy DB freeze.

.DESCRIPTION
This script is meant to run in CI while the team is explicitly avoiding production
schema changes. It blocks changes under EF migration folders and Supabase migration folders.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\check-phase2-db-freeze.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Push-Location $repoRoot

try {
    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $gitCommand) {
        throw "git was not found in PATH."
    }

    $baseRef = if ($env:GITHUB_BASE_REF) { "origin/$($env:GITHUB_BASE_REF)" } else { "HEAD~1" }
    $null = & git rev-parse --verify $baseRef 2>$null
    if ($LASTEXITCODE -ne 0) {
        $baseRef = "HEAD"
    }

    $changedFiles = @(& git diff --name-only "$baseRef...HEAD")
    if ($LASTEXITCODE -ne 0 -or $changedFiles.Count -eq 0) {
        $changedFiles = @(& git diff --name-only HEAD)
    }

    $blockedPatterns = @(
        '^JurisFlow\.Server/Migrations/',
        '^supabase/migrations/'
    )

    $violations = @(
        foreach ($file in $changedFiles) {
        foreach ($pattern in $blockedPatterns) {
            if ($file -match $pattern) {
                $file
                break
            }
        }
        }
    )

    if ($violations.Count -gt 0) {
        Write-Error @"
Phase 2 legacy DB freeze is active.
The following schema/migration paths were changed:
$($violations | Sort-Object -Unique | ForEach-Object { "- $_" } | Out-String)
Do not introduce DB migration files during the refactor wave.
"@
        exit 1
    }

    Write-Host "Phase 2 DB freeze check passed. No migration path changes were detected."
}
finally {
    Pop-Location
}
