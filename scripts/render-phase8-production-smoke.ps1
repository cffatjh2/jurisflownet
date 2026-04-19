<#
.SYNOPSIS
Builds or executes the Phase 8 production smoke plan.

.DESCRIPTION
Default mode is preview-only and writes a Markdown smoke plan for the production cutover.
When -Execute is supplied, the script runs critical HTTP probes against a production base URL.
This script does not change Render or Supabase configuration.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase8-production-smoke.ps1
#>

param(
    [string]$BaseUrl,
    [string]$TenantSlug,
    [string]$StaffEmail,
    [string]$StaffPassword,
    [string]$ClientEmail,
    [string]$ClientPassword,
    [switch]$Execute,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "documentation\PHASE8_PRODUCTION_SMOKE_PREVIEW.md"
}

function Write-PreviewReport {
    $content = @(
        "# Phase 8 Production Smoke Preview",
        "",
        "- mode: preview",
        "- execute-http: no",
        "- real-render-change: no",
        "- real-supabase-change: no",
        "",
        "## Critical Probes",
        "",
        '- `GET /health`',
        '- `POST /api/login`',
        '- `GET /api/Matters?page=1&pageSize=10`',
        '- `GET /api/Trust/accounts?limit=5`',
        '- `GET /api/Invoices`',
        '- `GET /api/matters/{matterId}/notes?page=1&pageSize=10`',
        '- `POST /api/client/login`',
        '- `GET /api/client/matters`',
        '- `GET /api/client/invoices`',
        "",
        "## Future Execute Parameters",
        "",
        '- `BaseUrl` : canonical production base url',
        '- `TenantSlug` : production tenant slug passed as `X-Tenant-Slug`',
        '- `StaffEmail` / `StaffPassword` : production-safe smoke account',
        '- `ClientEmail` / `ClientPassword` : optional portal smoke account',
        "",
        "## Notes",
        "",
        "- health yesil olmadan diger probelara gecilmez",
        "- staff login MFA required donerse blocker olarak kaydedilir",
        "- client credential verilmezse portal probelari skipped olarak raporlanir"
    )

    $content | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host ("Phase 8 smoke preview written to {0}" -f $OutputPath)
}

if (-not $Execute) {
    Write-PreviewReport
    return
}

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw "BaseUrl is required when -Execute is supplied."
}

$normalizedBaseUrl = $BaseUrl.TrimEnd("/")
$tenantHeaders = @{}
if (-not [string]::IsNullOrWhiteSpace($TenantSlug)) {
    $tenantHeaders["X-Tenant-Slug"] = $TenantSlug
}

function Invoke-SmokeRequest {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        [hashtable]$Headers,
        [object]$Body
    )

    $uri = "{0}{1}" -f $normalizedBaseUrl, $Path
    $params = @{
        Method = $Method
        Uri = $uri
        Headers = $Headers
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $params["ContentType"] = "application/json"
        $params["Body"] = ($Body | ConvertTo-Json -Depth 8)
    }

    try {
        $response = Invoke-RestMethod @params
        return [pscustomobject]@{
            Name = $Name
            Success = $true
            StatusCode = 200
            Detail = "ok"
            Body = $response
        }
    }
    catch {
        $statusCode = 0
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        return [pscustomobject]@{
            Name = $Name
            Success = $false
            StatusCode = $statusCode
            Detail = $_.Exception.Message
            Body = $null
        }
    }
}

$results = New-Object System.Collections.Generic.List[object]
$health = Invoke-SmokeRequest -Name "health" -Method "GET" -Path "/health" -Headers $tenantHeaders -Body $null
$results.Add($health)

if (-not $health.Success) {
    $results.Add([pscustomobject]@{
        Name = "cutover_blocker"
        Success = $false
        StatusCode = $health.StatusCode
        Detail = "health_failed"
        Body = $null
    })
}
else {
    $staffToken = $null
    $firstMatterId = $null
    $clientToken = $null

    if (-not [string]::IsNullOrWhiteSpace($StaffEmail) -and -not [string]::IsNullOrWhiteSpace($StaffPassword)) {
        $staffLogin = Invoke-SmokeRequest -Name "staff_login" -Method "POST" -Path "/api/login" -Headers $tenantHeaders -Body @{
            email = $StaffEmail
            password = $StaffPassword
        }
        $results.Add($staffLogin)

        if ($staffLogin.Success -and $null -ne $staffLogin.Body) {
            if ($staffLogin.Body.mfaRequired -eq $true) {
                $results.Add([pscustomobject]@{
                    Name = "staff_probe_blocker"
                    Success = $false
                    StatusCode = 200
                    Detail = "mfa_required"
                    Body = $null
                })
            }
            else {
                $staffToken = [string]$staffLogin.Body.token
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($staffToken)) {
        $staffHeaders = @{}
        foreach ($item in $tenantHeaders.GetEnumerator()) {
            $staffHeaders[$item.Key] = $item.Value
        }
        $staffHeaders["Authorization"] = "Bearer $staffToken"

        $matters = Invoke-SmokeRequest -Name "matters_list" -Method "GET" -Path "/api/Matters?page=1&pageSize=10" -Headers $staffHeaders -Body $null
        $results.Add($matters)

        if ($matters.Success -and $null -ne $matters.Body) {
            $matterCollection = @($matters.Body)
            if ($matterCollection.Count -gt 0) {
                $candidate = $matterCollection[0]
                if ($candidate.PSObject.Properties.Name -contains "id") {
                    $firstMatterId = [string]$candidate.id
                }
                elseif ($candidate.PSObject.Properties.Name -contains "Id") {
                    $firstMatterId = [string]$candidate.Id
                }
            }
        }

        $results.Add((Invoke-SmokeRequest -Name "trust_accounts" -Method "GET" -Path "/api/Trust/accounts?limit=5" -Headers $staffHeaders -Body $null))
        $results.Add((Invoke-SmokeRequest -Name "invoices_list" -Method "GET" -Path "/api/Invoices" -Headers $staffHeaders -Body $null))

        if (-not [string]::IsNullOrWhiteSpace($firstMatterId)) {
            $results.Add((Invoke-SmokeRequest -Name "matter_notes_list" -Method "GET" -Path "/api/matters/$firstMatterId/notes?page=1&pageSize=10" -Headers $staffHeaders -Body $null))
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ClientEmail) -and -not [string]::IsNullOrWhiteSpace($ClientPassword)) {
        $clientLogin = Invoke-SmokeRequest -Name "client_login" -Method "POST" -Path "/api/client/login" -Headers $tenantHeaders -Body @{
            email = $ClientEmail
            password = $ClientPassword
        }
        $results.Add($clientLogin)

        if ($clientLogin.Success -and $null -ne $clientLogin.Body) {
            $clientToken = [string]$clientLogin.Body.token
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($clientToken)) {
        $clientHeaders = @{}
        foreach ($item in $tenantHeaders.GetEnumerator()) {
            $clientHeaders[$item.Key] = $item.Value
        }
        $clientHeaders["Authorization"] = "Bearer $clientToken"

        $results.Add((Invoke-SmokeRequest -Name "client_matters" -Method "GET" -Path "/api/client/matters" -Headers $clientHeaders -Body $null))
        $results.Add((Invoke-SmokeRequest -Name "client_invoices" -Method "GET" -Path "/api/client/invoices" -Headers $clientHeaders -Body $null))
    }
}

$content = @(
    "# Phase 8 Production Smoke Report",
    "",
    "- mode: execute",
    "- base-url: $normalizedBaseUrl",
    "- tenant-slug: $(if ([string]::IsNullOrWhiteSpace($TenantSlug)) { "not-set" } else { $TenantSlug })",
    "- real-render-change: no",
    "- real-supabase-change: no",
    "",
    "## Results",
    ""
)

foreach ($item in $results) {
    $content += ('- `{0}` => success={1}; status={2}; detail={3}' -f $item.Name, $item.Success, $item.StatusCode, $item.Detail)
}

$content += @(
    "",
    "## Verdict",
    ""
)

$failedItems = @($results | Where-Object { -not $_.Success }).Count
if ($failedItems -eq 0) {
    $content += "- phase8-smoke: passed"
}
else {
    $content += "- phase8-smoke: attention-required"
}

$content | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host ("Phase 8 smoke report written to {0}" -f $OutputPath)
