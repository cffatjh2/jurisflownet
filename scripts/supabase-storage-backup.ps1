param(
    [Parameter(Mandatory = $true)]
    [string]$SupabaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$ServiceRoleKey,

    [Parameter(Mandatory = $true)]
    [string]$Bucket,

    [string]$Prefix = "",

    [string]$OutputDirectory = ".\\out\\supabase-storage-backups"
)

$ErrorActionPreference = "Stop"

function New-SupabaseHeaders {
    return @{
        "Authorization" = "Bearer $ServiceRoleKey"
        "apikey" = $ServiceRoleKey
    }
}

function Get-ListPage {
    param(
        [string]$CurrentPrefix,
        [int]$Offset,
        [int]$Limit
    )

    $uri = "{0}/storage/v1/object/list/{1}" -f $SupabaseUrl.TrimEnd('/'), [System.Uri]::EscapeDataString($Bucket)
    $body = @{
        prefix = $CurrentPrefix
        limit = $Limit
        offset = $Offset
        sortBy = @{
            column = "name"
            order = "asc"
        }
    } | ConvertTo-Json -Depth 4

    Invoke-RestMethod -Method Post -Uri $uri -Headers (New-SupabaseHeaders) -ContentType "application/json" -Body $body
}

function Get-DownloadUri {
    param([string]$ObjectPath)

    $segments = $ObjectPath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [System.Uri]::EscapeDataString($_) }
    $escapedPath = [string]::Join('/', $segments)
    return "{0}/storage/v1/object/authenticated/{1}/{2}" -f $SupabaseUrl.TrimEnd('/'), [System.Uri]::EscapeDataString($Bucket), $escapedPath
}

function Get-ObjectEntries {
    param([string]$CurrentPrefix)

    $offset = 0
    $limit = 100
    $results = New-Object System.Collections.Generic.List[object]

    while ($true) {
        $page = @(Get-ListPage -CurrentPrefix $CurrentPrefix -Offset $offset -Limit $limit)
        if ($page.Count -eq 0) {
            break
        }

        foreach ($item in $page) {
            $name = [string]$item.name
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            $isFolder = ($null -eq $item.id) -and ($null -eq $item.metadata)
            $objectPath = if ([string]::IsNullOrWhiteSpace($CurrentPrefix)) { $name } else { "$CurrentPrefix/$name" }

            if ($isFolder) {
                foreach ($child in Get-ObjectEntries -CurrentPrefix $objectPath) {
                    [void]$results.Add($child)
                }
            }
            else {
                [void]$results.Add([pscustomobject]@{
                    path = $objectPath.Replace('\', '/')
                    metadata = $item.metadata
                    updatedAt = $item.updated_at
                })
            }
        }

        if ($page.Count -lt $limit) {
            break
        }

        $offset += $limit
    }

    return $results
}

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $resolvedOutputDirectory "storage-$timestamp"
$objectRoot = Join-Path $backupRoot "objects"
New-Item -ItemType Directory -Force -Path $objectRoot | Out-Null

$entries = @(Get-ObjectEntries -CurrentPrefix $Prefix.Trim('/'))

foreach ($entry in $entries) {
    $targetPath = Join-Path $objectRoot ($entry.path.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    $targetDirectory = Split-Path -Parent $targetPath
    if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    }

    Invoke-WebRequest -Method Get -Uri (Get-DownloadUri -ObjectPath $entry.path) -Headers (New-SupabaseHeaders) -OutFile $targetPath | Out-Null
}

$manifest = [pscustomobject]@{
    bucket = $Bucket
    prefix = $Prefix
    createdAt = [DateTime]::UtcNow.ToString("o")
    objectCount = $entries.Count
    objects = $entries
}

$manifestPath = Join-Path $backupRoot "manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Storage backup completed: $backupRoot"
