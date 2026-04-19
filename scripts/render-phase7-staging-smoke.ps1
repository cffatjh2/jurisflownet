<#
.SYNOPSIS
Builds or executes the Phase 7 staging smoke plan for the canonical cutover.

.DESCRIPTION
Default mode is preview-only and writes a Markdown smoke plan from the manifest without making
network calls. When -Execute is supplied, the script runs HTTP probes against a staging base URL.
This script does not create databases, push migrations, or modify Render configuration.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-staging-smoke.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-staging-smoke.ps1 -Execute -BaseUrl https://staging.example.com -TenantSlug demo -StaffEmail admin@example.com -StaffPassword secret
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
$manifestPath = Join-Path $repoRoot "documentation\PHASE7_STAGING_SMOKE_MANIFEST.json"
$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "documentation\PHASE7_STAGING_SMOKE_PREVIEW.md"
}

function Write-PreviewReport {
    $content = @(
        "# Phase 7 Staging Smoke Preview",
        "",
        "- mode: preview",
        "- execute-http: no",
        "- real-render-change: no",
        "- real-supabase-change: no",
        "",
        "## Smoke Matrix",
        ""
    )

    foreach ($surface in $manifest.surfaces) {
        $content += ('- `{0}` : {1} {2} [{3}] - {4}' -f $surface.id, $surface.method, $surface.path, $surface.auth, $surface.notes)
    }

    $content += @(
        "",
        "## Future Execute Parameters",
        "",
        '- `BaseUrl` : canonical staging base url',
        '- `TenantSlug` : staging tenant slug passed as `X-Tenant-Slug`',
        '- `StaffEmail` / `StaffPassword` : seeded staff smoke account',
        '- `ClientEmail` / `ClientPassword` : optional portal smoke account',
        "",
        "## Notes",
        "",
        "- staff login MFA required donerse staff probes atlanir",
        '- note probe ilk matter kaydindan `matterId` turetir',
        "- client credential verilmezse portal probelari skipped olarak raporlanir"
    )

    $content | Set-Content -Path $OutputPath -Encoding UTF8
    Write-Host ("Phase 7 smoke preview written to {0}" -f $OutputPath)
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

function ConvertTo-Hashtable($Object) {
    $table = @{}
    if ($null -eq $Object) {
        return $table
    }

    $Object.PSObject.Properties | ForEach-Object {
        $table[$_.Name] = $_.Value
    }

    return $table
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
            Method = $Method
            Path = $Path
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
            Method = $Method
            Path = $Path
            Success = $false
            StatusCode = $statusCode
            Detail = $_.Exception.Message
            Body = $null
        }
    }
}

$results = New-Object System.Collections.Generic.List[object]

$results.Add((Invoke-SmokeRequest -Name "health" -Method "GET" -Path "/health" -Headers $tenantHeaders -Body $null))

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
                Method = "POST"
                Path = "/api/login"
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
else {
    $results.Add([pscustomobject]@{
        Name = "staff_login"
        Method = "POST"
        Path = "/api/login"
        Success = $false
        StatusCode = 0
        Detail = "skipped_missing_staff_credentials"
        Body = $null
    })
}

if (-not [string]::IsNullOrWhiteSpace($staffToken)) {
    $staffHeaders = ConvertTo-Hashtable $tenantHeaders
    $staffHeaders["Authorization"] = "Bearer $staffToken"

    $mattersResult = Invoke-SmokeRequest -Name "matters_list" -Method "GET" -Path "/api/Matters?page=1&pageSize=10" -Headers $staffHeaders -Body $null
    $results.Add($mattersResult)

    if ($mattersResult.Success -and $null -ne $mattersResult.Body) {
        $matterCollection = @($mattersResult.Body)
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

    if (-not [string]::IsNullOrWhiteSpace($firstMatterId)) {
        $results.Add((Invoke-SmokeRequest -Name "matter_notes_list" -Method "GET" -Path "/api/matters/$firstMatterId/notes?page=1&pageSize=10" -Headers $staffHeaders -Body $null))
    }
    else {
        $results.Add([pscustomobject]@{
            Name = "matter_notes_list"
            Method = "GET"
            Path = "/api/matters/{matterId}/notes?page=1&pageSize=10"
            Success = $false
            StatusCode = 0
            Detail = "skipped_no_matter_id_available"
            Body = $null
        })
    }

    $results.Add((Invoke-SmokeRequest -Name "trust_accounts" -Method "GET" -Path "/api/Trust/accounts?limit=5" -Headers $staffHeaders -Body $null))
    $results.Add((Invoke-SmokeRequest -Name "invoices_list" -Method "GET" -Path "/api/Invoices" -Headers $staffHeaders -Body $null))
    $results.Add((Invoke-SmokeRequest -Name "documents_list" -Method "GET" -Path "/api/Documents" -Headers $staffHeaders -Body $null))
    $results.Add((Invoke-SmokeRequest -Name "integrations_contract" -Method "GET" -Path "/api/integrations/ops/contract" -Headers $staffHeaders -Body $null))
    $results.Add((Invoke-SmokeRequest -Name "integrations_capability_matrix" -Method "GET" -Path "/api/integrations/ops/capability-matrix" -Headers $staffHeaders -Body $null))
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
else {
    $results.Add([pscustomobject]@{
        Name = "client_login"
        Method = "POST"
        Path = "/api/client/login"
        Success = $false
        StatusCode = 0
        Detail = "skipped_missing_client_credentials"
        Body = $null
    })
}

if (-not [string]::IsNullOrWhiteSpace($clientToken)) {
    $clientHeaders = ConvertTo-Hashtable $tenantHeaders
    $clientHeaders["Authorization"] = "Bearer $clientToken"

    $results.Add((Invoke-SmokeRequest -Name "client_profile" -Method "GET" -Path "/api/client/profile" -Headers $clientHeaders -Body $null))
    $results.Add((Invoke-SmokeRequest -Name "client_matters" -Method "GET" -Path "/api/client/matters" -Headers $clientHeaders -Body $null))
    $results.Add((Invoke-SmokeRequest -Name "client_invoices" -Method "GET" -Path "/api/client/invoices" -Headers $clientHeaders -Body $null))
    $results.Add((Invoke-SmokeRequest -Name "client_documents" -Method "GET" -Path "/api/client/documents" -Headers $clientHeaders -Body $null))
}

$content = @(
    "# Phase 7 Staging Smoke Report",
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
    $content += ('- `{0}` : {1} {2} => success={3}; status={4}; detail={5}' -f $item.Name, $item.Method, $item.Path, $item.Success, $item.StatusCode, $item.Detail)
}

$content += @(
    "",
    "## Verdict",
    ""
)

$failedItems = @($results | Where-Object { -not $_.Success -and $_.Detail -notlike "skipped_*" }).Count
if ($failedItems -eq 0) {
    $content += "- phase7-smoke: passed-or-skipped"
}
else {
    $content += "- phase7-smoke: attention-required"
}

$content | Set-Content -Path $OutputPath -Encoding UTF8
Write-Host ("Phase 7 smoke report written to {0}" -f $OutputPath)
