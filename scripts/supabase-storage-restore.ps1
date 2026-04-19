param(
    [Parameter(Mandatory = $true)]
    [string]$SupabaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$ServiceRoleKey,

    [Parameter(Mandatory = $true)]
    [string]$Bucket,

    [Parameter(Mandatory = $true)]
    [string]$BackupDirectory,

    [switch]$Apply
)

$ErrorActionPreference = "Stop"

function New-SupabaseHeaders {
    return @{
        "Authorization" = "Bearer $ServiceRoleKey"
        "apikey" = $ServiceRoleKey
    }
}

function Get-ObjectInfo {
    param([string]$ObjectPath)

    $segments = $ObjectPath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [System.Uri]::EscapeDataString($_) }
    $escapedPath = [string]::Join('/', $segments)
    $uri = "{0}/storage/v1/object/info/{1}/{2}" -f $SupabaseUrl.TrimEnd('/'), [System.Uri]::EscapeDataString($Bucket), $escapedPath

    try {
        Invoke-RestMethod -Method Get -Uri $uri -Headers (New-SupabaseHeaders) | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -eq 404) {
            return $false
        }

        throw
    }
}

function Ensure-Bucket {
    $headers = New-SupabaseHeaders
    $bucketUri = "{0}/storage/v1/bucket/{1}" -f $SupabaseUrl.TrimEnd('/'), [System.Uri]::EscapeDataString($Bucket)

    try {
        Invoke-RestMethod -Method Get -Uri $bucketUri -Headers $headers | Out-Null
        return
    }
    catch {
        if (-not $_.Exception.Response -or $_.Exception.Response.StatusCode.value__ -ne 404) {
            throw
        }
    }

    $createUri = "{0}/storage/v1/bucket" -f $SupabaseUrl.TrimEnd('/')
    $body = @{ id = $Bucket; name = $Bucket; public = $false } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri $createUri -Headers $headers -ContentType "application/json" -Body $body | Out-Null
}

function Get-UploadUri {
    param([string]$ObjectPath)

    $segments = $ObjectPath.Split('/', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { [System.Uri]::EscapeDataString($_) }
    $escapedPath = [string]::Join('/', $segments)
    return "{0}/storage/v1/object/{1}/{2}" -f $SupabaseUrl.TrimEnd('/'), [System.Uri]::EscapeDataString($Bucket), $escapedPath
}

$resolvedBackupDirectory = [System.IO.Path]::GetFullPath($BackupDirectory)
$objectRoot = Join-Path $resolvedBackupDirectory "objects"

if (-not (Test-Path -LiteralPath $objectRoot)) {
    throw "Backup directory does not contain an 'objects' folder: $resolvedBackupDirectory"
}

$files = Get-ChildItem -Path $objectRoot -File -Recurse
$objects = $files | ForEach-Object {
    [pscustomobject]@{
        FullName = $_.FullName
        RelativePath = [System.IO.Path]::GetRelativePath($objectRoot, $_.FullName).Replace('\', '/')
        SizeBytes = $_.Length
    }
}

if (-not $Apply) {
    $totalBytes = ($objects | Measure-Object -Property SizeBytes -Sum).Sum
    Write-Host "Dry run complete."
    Write-Host ("Objects queued for restore: {0}" -f $objects.Count)
    Write-Host ("Total bytes queued: {0}" -f ($totalBytes ?? 0))
    exit 0
}

Ensure-Bucket

foreach ($object in $objects) {
    $exists = Get-ObjectInfo -ObjectPath $object.RelativePath
    $method = if ($exists) { "Put" } else { "Post" }
    $uri = Get-UploadUri -ObjectPath $object.RelativePath
    $headers = New-SupabaseHeaders

    Invoke-WebRequest -Method $method -Uri $uri -Headers $headers -InFile $object.FullName -ContentType "application/octet-stream" | Out-Null
}

Write-Host ("Storage restore applied for {0} objects." -f $objects.Count)
